
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Events;
using OrderService.Models;

namespace OrderService.Services;

public sealed class PaymentEventsConsumer : BackgroundService
{
    private const int MaxAttempts = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentEventsConsumer> _logger;
    private readonly IConfiguration _configuration;

    public PaymentEventsConsumer(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentEventsConsumer> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var bootstrapServers =
            _configuration["Kafka:BootstrapServers"]
            ?? "localhost:9092";

        var topic =
            _configuration["Kafka:PaymentEventsTopic"]
            ?? "payment-events";

        var dltTopic =
            _configuration["Kafka:DeadLetterTopic"]
            ?? "payment-events-dlt";

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = _configuration["Kafka:ConsumerGroup"]
                ?? "scaleorder-order-service",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        };

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = "scaleorder-order-dlt-publisher",
            Acks = Acks.All,
            EnableIdempotence = true
        };

        using var consumer =
            new ConsumerBuilder<string, string>(consumerConfig).Build();

        using var producer =
            new ProducerBuilder<string, string>(producerConfig).Build();

        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Payment events consumer started. Topic: {Topic}, DLT: {DltTopic}",
            topic, dltTopic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;

                try
                {
                    result = consumer.Consume(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed.");
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                var processed = false;
                Exception? lastError = null;

                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    try
                    {
                        var wasProcessed = await ProcessEventAsync(
                            result.Message.Value,
                            stoppingToken);

                        if (wasProcessed)
                        {
                            _logger.LogInformation(
                                "Payment event processed. Topic: {Topic}, " +
                                "Partition: {Partition}, Offset: {Offset}",
                                result.Topic,
                                result.Partition.Value,
                                result.Offset.Value);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "Duplicate payment event skipped. " +
                                "Topic: {Topic}, Offset: {Offset}",
                                result.Topic, result.Offset.Value);
                        }

                        processed = true;
                        break;
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;

                        _logger.LogWarning(
                            ex,
                            "Payment event attempt {Attempt}/{MaxAttempts} failed. " +
                            "Partition: {Partition}, Offset: {Offset}",
                            attempt,
                            MaxAttempts,
                            result.Partition.Value,
                            result.Offset.Value);

                        if (attempt < MaxAttempts)
                        {
                            await Task.Delay(
                                TimeSpan.FromSeconds(2), stoppingToken);
                        }
                    }
                }

                if (!processed)
                {
                    var deadLetterMessage = JsonSerializer.Serialize(new
                    {
                        OriginalTopic = result.Topic,
                        Partition = result.Partition.Value,
                        Offset = result.Offset.Value,
                        OriginalKey = result.Message.Key,
                        OriginalPayload = result.Message.Value,
                        FailedAt = DateTime.UtcNow,
                        Attempts = MaxAttempts,
                        Error = lastError?.ToString()
                    });

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        try
                        {
                            await producer.ProduceAsync(
                                dltTopic,
                                new Message<string, string>
                                {
                                    Key = result.Message.Key
                                        ?? $"{result.Partition.Value}:{result.Offset.Value}",
                                    Value = deadLetterMessage
                                },
                                stoppingToken);

                            _logger.LogError(
                                "Payment event sent to DLT after {Attempts} attempts. " +
                                "Original partition: {Partition}, offset: {Offset}",
                                MaxAttempts,
                                result.Partition.Value,
                                result.Offset.Value);

                            break;
                        }
                        catch (OperationCanceledException)
                            when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Failed to publish to DLT {DltTopic}. " +
                                "Will retry without committing the offset.",
                                dltTopic);

                            await Task.Delay(
                                TimeSpan.FromSeconds(3), stoppingToken);
                        }
                    }

                    stoppingToken.ThrowIfCancellationRequested();
                }

                // Commit only after processing or publishing to DLT.
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal service shutdown.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task<bool> ProcessEventAsync(
        string payload,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (!TryGetPropertyIgnoreCase(
                root, "EventType", out var eventTypeElement))
        {
            throw new InvalidOperationException(
                "Payment event is missing EventType.");
        }

        var eventType = eventTypeElement.GetString();

        if (eventType != "PaymentSucceeded" &&
            eventType != "PaymentFailed")
        {
            throw new InvalidOperationException(
                $"Unsupported payment event type: {eventType}");
        }

        if (!TryGetPropertyIgnoreCase(
                root, "EventId", out var eventIdElement) ||
            !eventIdElement.TryGetGuid(out var eventId) ||
            eventId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "Payment event has an invalid EventId.");
        }

        using var scope = _scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<OrderDbContext>();

        var alreadyProcessed = await dbContext.ProcessedEvents
            .AnyAsync(
                e => e.EventId == eventId,
                cancellationToken);

        if (alreadyProcessed)
            return false;

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                cancellationToken);

        if (eventType == "PaymentSucceeded")
        {
            var paymentEvent =
                JsonSerializer.Deserialize<PaymentSucceededEvent>(
                    payload,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (paymentEvent == null ||
                paymentEvent.EventId == Guid.Empty ||
                paymentEvent.OrderId == Guid.Empty ||
                paymentEvent.PaymentId == Guid.Empty ||
                paymentEvent.UserId == Guid.Empty ||
                paymentEvent.Amount <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid PaymentSucceeded event payload.");
            }

            var orderService = scope.ServiceProvider
                .GetRequiredService<IOrderService>();

            var markedPaid = await orderService.MarkOrderAsPaidAsync(
                paymentEvent.OrderId);

            if (!markedPaid)
            {
                throw new InvalidOperationException(
                    $"Could not mark order {paymentEvent.OrderId} as paid.");
            }
        }
        else
        {
            // A failed payment is recorded for deduplication.
            // Do not change the order to Paid.
            _logger.LogInformation(
                "PaymentFailed event received. Order remains unpaid. " +
                "EventId: {EventId}",
                eventId);
        }

        dbContext.ProcessedEvents.Add(new ProcessedEvent
        {
            EventId = eventId,
            EventType = eventType,
            ProcessedAt = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
