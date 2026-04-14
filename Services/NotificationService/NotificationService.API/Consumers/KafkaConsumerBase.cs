using Confluent.Kafka;
using System.Text.Json;

namespace NotificationService.API.Consumers
{
    public abstract class KafkaConsumerBase<T> : BackgroundService where T : class
    {
        private readonly IConsumer<string, string> _consumer;
        private readonly ILogger _logger;

        protected abstract string Topic { get; }
        protected abstract string GroupId { get; }
        protected KafkaConsumerBase(IConfiguration configuration, ILogger logger)
        {
            _logger = logger;
            var config = new ConsumerConfig
            {
                BootstrapServers = configuration["Kafka:BootstrapServers"],
                GroupId = GroupId,
                AutoOffsetReset = AutoOffsetReset.Earliest
            };
            _consumer = new ConsumerBuilder<string, string>(config).Build();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _consumer.Subscribe(Topic);
            _logger.LogInformation("Kafka consumer subscribed to topic: {Topic}", Topic);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = _consumer.Consume(stoppingToken);
                    var message = JsonSerializer.Deserialize<T>(consumeResult.Message.Value);
                    if (message != null)
                    {
                        await ProcessMessageAsync(message);
                        _consumer.Commit(consumeResult);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error consuming message from topic {Topic}", Topic);
                }
            }
        }
        protected abstract Task ProcessMessageAsync(T message);

        public override void Dispose()
        {
            _consumer?.Close();
            _consumer?.Dispose();
            base.Dispose();
        }
    }
}

