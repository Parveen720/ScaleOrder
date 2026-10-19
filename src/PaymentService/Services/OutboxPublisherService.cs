
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;

namespace PaymentService.Services;

public sealed class OutboxPublisherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public OutboxPublisherService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<OutboxPublisherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var bootstrapServers =
            configuration["Kafka:BootstrapServers"]
            ?? "localhost:9092";

        _topic =
            configuration["Kafka:PaymentEventsTopic"]
            ?? "payment-events";

        var producerConfig = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            ClientId = "scaleorder-payment-outbox-publisher",
            Acks = Acks.All,
            EnableIdempotence = true
        };

        _producer = new ProducerBuilder<string, string>(
            producerConfig).Build();
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox publisher started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingMessagesAsync(stoppingToken);
                await Task.Delay(
                    TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Outbox publisher iteration failed.");

                await Task.Delay(
                    TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task PublishPendingMessagesAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var dbContext = scope.ServiceProvider
            .GetRequiredService<PaymentDbContext>();

        var messages = await dbContext.OutboxMessages
            .Where(message => message.ProcessedAt == null)
            .OrderBy(message => message.OccurredAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var kafkaMessage = new Message<string, string>
                {
                    Key = message.Id.ToString(),
                    Value = message.Payload
                };

                await _producer.ProduceAsync(
                    _topic, kafkaMessage, cancellationToken);

                message.ProcessedAt = DateTime.UtcNow;
                message.Error = null;

                await dbContext.SaveChangesAsync(
                    cancellationToken);

                _logger.LogInformation(
                    "Outbox event published. EventId: {EventId}, " +
                    "Type: {EventType}, Topic: {Topic}",
                    message.Id, message.EventType, _topic);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message.Length > 4000
                    ? ex.Message[..4000]
                    : ex.Message;

                await dbContext.SaveChangesAsync(
                    cancellationToken);

                _logger.LogError(
                    ex,
                    "Failed to publish outbox event {EventId}. " +
                    "Retry count: {RetryCount}",
                    message.Id, message.RetryCount);
            }
        }
    }

    public override void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        base.Dispose();
    }
}
