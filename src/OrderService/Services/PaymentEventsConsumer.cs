
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using OrderService.Data;
using OrderService.Events;
using OrderService.Models;

namespace OrderService.Services;

public sealed class PaymentEventsConsumer : BackgroundService
{
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
        var config = new ConsumerConfig
        {
            BootstrapServers =
                _configuration["Kafka:BootstrapServers"]
                ?? "localhost:9092",

            GroupId =
                _configuration["Kafka:ConsumerGroup"]
                ?? "scaleorder-order-service",

            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer =
            new ConsumerBuilder<string, string>(config).Build();

        var topic =
            _configuration["Kafka:PaymentEventsTopic"]
            ?? "payment-events";

        consumer.Subscribe(topic);

        _logger.LogInformation(
            "Payment events consumer started. Topic: {Topic}",
            topic);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = consumer.Consume(stoppingToken);

                    var paymentEvent =
                        JsonSerializer.Deserialize<PaymentSucceededEvent>(
                            result.Message.Value,
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

                    using var scope = _scopeFactory.CreateScope();

                    var dbContext = scope.ServiceProvider
                        .GetRequiredService<OrderDbContext>();

                    var orderService = scope.ServiceProvider
                        .GetRequiredService<IOrderService>();

                    // A previously processed event must not be applied again.
                    var alreadyProcessed = await dbContext.ProcessedEvents
                        .AnyAsync(
                            e => e.EventId == paymentEvent.EventId,
                            stoppingToken);

                    if (alreadyProcessed)
                    {
                        _logger.LogInformation(
                            "Duplicate payment event skipped. EventId: {EventId}",
                            paymentEvent.EventId);

                        consumer.Commit(result);
                        continue;
                    }

                    // Both the order update and event record use
                    // the same OrderDbContext and database transaction.
                    await using var transaction =
                        await dbContext.Database.BeginTransactionAsync(
                            stoppingToken);

                    var markedPaid =
                        await orderService.MarkOrderAsPaidAsync(
                            paymentEvent.OrderId);

                    if (!markedPaid)
                    {
                        throw new InvalidOperationException(
                            $"Could not mark order {paymentEvent.OrderId} as paid.");
                    }

                    dbContext.ProcessedEvents.Add(new ProcessedEvent
                    {
                        EventId = paymentEvent.EventId,
                        EventType = "PaymentSucceeded",
                        ProcessedAt = DateTime.UtcNow
                    });

                    await dbContext.SaveChangesAsync(stoppingToken);

                    await transaction.CommitAsync(stoppingToken);

                    // Commit Kafka offset only after the database transaction.
                    consumer.Commit(result);

                    _logger.LogInformation(
                        "Payment event processed successfully. " +
                        "EventId: {EventId}, OrderId: {OrderId}",
                        paymentEvent.EventId,
                        paymentEvent.OrderId);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed.");

                    await Task.Delay(
                        TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Payment event processing failed. " +
                        "The Kafka offset was not committed.");

                    await Task.Delay(
                        TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }
}
