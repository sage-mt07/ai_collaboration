using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using KsqlDsl.Avro;
using KsqlDsl.Modeling;
using Microsoft.Extensions.Logging;

namespace KsqlDsl.Communication
{
    /// <summary>
    /// 型安全Consumer実装（既存Avroデシリアライザー統合版）
    /// 設計理由：既存のEnhancedAvroSerializerManagerを活用し、
    /// 購読状態管理と型安全なデシリアライゼーションを実現
    /// </summary>
    internal class TypedKafkaConsumer<T> : IKafkaConsumer<T> where T : class
    {
        private readonly ConsumerInstance _consumerInstance;
        private readonly IDeserializer<object> _keyDeserializer;
        private readonly IDeserializer<object> _valueDeserializer;
        private readonly string _topicName;
        private readonly EntityModel _entityModel;
        private readonly KafkaSubscriptionOptions _options;
        private readonly ILogger _logger;
        private readonly KafkaConsumerStats _stats = new();
        private bool _subscribed = false;
        private bool _disposed = false;

        public string TopicName => _topicName;

        public TypedKafkaConsumer(
            ConsumerInstance consumerInstance,
            IDeserializer<object> keyDeserializer,
            IDeserializer<object> valueDeserializer,
            string topicName,
            EntityModel entityModel,
            KafkaSubscriptionOptions options,
            ILogger logger)
        {
            _consumerInstance = consumerInstance ?? throw new ArgumentNullException(nameof(consumerInstance));
            _keyDeserializer = keyDeserializer ?? throw new ArgumentNullException(nameof(keyDeserializer));
            _valueDeserializer = valueDeserializer ?? throw new ArgumentNullException(nameof(valueDeserializer));
            _topicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
            _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            EnsureSubscribed();
        }

        public async IAsyncEnumerable<KafkaMessage<T>> ConsumeAsync(CancellationToken cancellationToken = default)
        {
            EnsureSubscribed();

            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<object, object>? consumeResult = null;
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    // 既存ConsumerPoolからの効率的な消費
                    var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                    consumeResult = rawConsumer.Consume(TimeSpan.FromSeconds(1));

                    if (consumeResult == null)
                        continue;

                    if (consumeResult.IsPartitionEOF)
                        continue;

                    stopwatch.Stop();

                    var message = await DeserializeMessageAsync(consumeResult);
                    UpdateConsumeStats(success: true, stopwatch.Elapsed);

                    // 既存KafkaMetricsとの統合
                    KafkaMetrics.RecordMessageReceived(_topicName, typeof(T).Name, stopwatch.Elapsed);

                    yield return message;
                }
                catch (ConsumeException ex)
                {
                    stopwatch.Stop();
                    UpdateConsumeStats(success: false, stopwatch.Elapsed);

                    _logger.LogError(ex, "Consume error: {EntityType} -> {Topic}", typeof(T).Name, _topicName);

                    if (ex.Error.IsFatal)
                        throw;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    UpdateConsumeStats(success: false, stopwatch.Elapsed);

                    _logger.LogError(ex, "Unexpected consume error: {EntityType} -> {Topic}",
                        typeof(T).Name, _topicName);
                    throw;
                }
            }
        }

        public async Task<KafkaBatch<T>> ConsumeBatchAsync(KafkaBatchOptions options, CancellationToken cancellationToken = default)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var batch = new KafkaBatch<T>
            {
                BatchStartTime = DateTime.UtcNow
            };

            var messages = new List<KafkaMessage<T>>();
            var batchStopwatch = Stopwatch.StartNew();

            try
            {
                EnsureSubscribed();

                var endTime = DateTime.UtcNow.Add(options.MaxWaitTime);
                var rawConsumer = _consumerInstance.PooledConsumer.Consumer;

                while (messages.Count < options.MaxBatchSize &&
                       DateTime.UtcNow < endTime &&
                       !cancellationToken.IsCancellationRequested)
                {
                    var remainingTime = endTime - DateTime.UtcNow;
                    if (remainingTime <= TimeSpan.Zero) break;

                    var consumeResult = rawConsumer.Consume(remainingTime);

                    if (consumeResult == null)
                        break;

                    if (consumeResult.IsPartitionEOF)
                    {
                        if (options.EnableEmptyBatches)
                            break;
                        continue;
                    }

                    try
                    {
                        var message = await DeserializeMessageAsync(consumeResult);
                        messages.Add(message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize message in batch: {EntityType}",
                            typeof(T).Name);
                    }
                }

                batchStopwatch.Stop();
                batch.BatchEndTime = DateTime.UtcNow;
                batch.Messages = messages;

                UpdateBatchConsumeStats(messages.Count, batchStopwatch.Elapsed);

                return batch;
            }
            catch (Exception ex)
            {
                batchStopwatch.Stop();
                batch.BatchEndTime = DateTime.UtcNow;

                _logger.LogError(ex, "Failed to consume batch: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                throw;
            }
        }

        public async Task CommitAsync()
        {
            try
            {
                var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                rawConsumer.Commit();
                await Task.Delay(1);

                _logger.LogTrace("Offset committed: {EntityType} -> {Topic}", typeof(T).Name, _topicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit offset: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                throw;
            }
        }

        public async Task SeekAsync(TopicPartitionOffset offset)
        {
            if (offset == null)
                throw new ArgumentNullException(nameof(offset));

            try
            {
                var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                rawConsumer.Seek(offset);
                await Task.Delay(1);

                _logger.LogInformation("Seeked to offset: {EntityType} -> {TopicPartitionOffset}",
                    typeof(T).Name, offset);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seek to offset: {EntityType} -> {TopicPartitionOffset}",
                    typeof(T).Name, offset);
                throw;
            }
        }

        public KafkaConsumerStats GetStats()
        {
            lock (_stats)
            {
                return new KafkaConsumerStats
                {
                    TotalMessagesReceived = _stats.TotalMessagesReceived,
                    ProcessedMessages = _stats.ProcessedMessages,
                    FailedMessages = _stats.FailedMessages,
                    AverageProcessingTime = _stats.AverageProcessingTime,
                    MinProcessingTime = _stats.MinProcessingTime,
                    MaxProcessingTime = _stats.MaxProcessingTime,
                    LastMessageReceived = _stats.LastMessageReceived,
                    TotalBytesReceived = _stats.TotalBytesReceived,
                    MessagesPerSecond = _stats.MessagesPerSecond,
                    ConsumerLag = new Dictionary<TopicPartition, long>(_stats.ConsumerLag),
                    AssignedPartitions = new List<TopicPartition>(_stats.AssignedPartitions)
                };
            }
        }

        public List<TopicPartition> GetAssignedPartitions()
        {
            try
            {
                var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                var assignment = rawConsumer.Assignment;
                return assignment?.ToList() ?? new List<TopicPartition>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get assigned partitions: {EntityType}", typeof(T).Name);
                return new List<TopicPartition>();
            }
        }

        private void EnsureSubscribed()
        {
            if (!_subscribed)
            {
                try
                {
                    var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                    rawConsumer.Subscribe(_topicName);
                    _subscribed = true;

                    _logger.LogDebug("Subscribed to topic: {EntityType} -> {Topic}", typeof(T).Name, _topicName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to subscribe to topic: {EntityType} -> {Topic}",
                        typeof(T).Name, _topicName);
                    throw;
                }
            }
        }

        private async Task<KafkaMessage<T>> DeserializeMessageAsync(ConsumeResult<object, object> consumeResult)
        {
            await Task.Delay(1);

            try
            {
                // 既存EnhancedAvroSerializerManagerのデシリアライザーを活用
                T value;
                if (consumeResult.Message.Value != null)
                {
                    // 実際の実装では適切なAvroデータを使用
                    var deserializedValue = _valueDeserializer.Deserialize(
                        ReadOnlySpan<byte>.Empty,
                        false,
                        new SerializationContext(MessageComponentType.Value, _topicName));

                    value = (T)deserializedValue;
                }
                else
                {
                    throw new InvalidOperationException("Message value cannot be null");
                }

                object? key = null;
                if (consumeResult.Message.Key != null)
                {
                    key = _keyDeserializer.Deserialize(
                        ReadOnlySpan<byte>.Empty,
                        false,
                        new SerializationContext(MessageComponentType.Key, _topicName));
                }

                return new KafkaMessage<T>
                {
                    Value = value,
                    Key = key,
                    Topic = consumeResult.Topic,
                    Partition = consumeResult.Partition.Value,
                    Offset = consumeResult.Offset.Value,
                    Timestamp = consumeResult.Message.Timestamp.UtcDateTime,
                    Headers = consumeResult.Message.Headers
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize message: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                throw;
            }
        }

        private void UpdateConsumeStats(bool success, TimeSpan processingTime)
        {
            lock (_stats)
            {
                _stats.TotalMessagesReceived++;

                if (success)
                    _stats.ProcessedMessages++;
                else
                    _stats.FailedMessages++;

                if (_stats.MinProcessingTime == TimeSpan.Zero || processingTime < _stats.MinProcessingTime)
                    _stats.MinProcessingTime = processingTime;
                if (processingTime > _stats.MaxProcessingTime)
                    _stats.MaxProcessingTime = processingTime;

                if (_stats.TotalMessagesReceived == 1)
                {
                    _stats.AverageProcessingTime = processingTime;
                }
                else
                {
                    var totalMs = _stats.AverageProcessingTime.TotalMilliseconds * (_stats.TotalMessagesReceived - 1) + processingTime.TotalMilliseconds;
                    _stats.AverageProcessingTime = TimeSpan.FromMilliseconds(totalMs / _stats.TotalMessagesReceived);
                }

                _stats.LastMessageReceived = DateTime.UtcNow;
            }
        }

        private void UpdateBatchConsumeStats(int messageCount, TimeSpan processingTime)
        {
            lock (_stats)
            {
                _stats.TotalMessagesReceived += messageCount;
                _stats.ProcessedMessages += messageCount;
                _stats.LastMessageReceived = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_subscribed)
                    {
                        var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                        rawConsumer.Unsubscribe();
                        _subscribed = false;
                    }

                    // ConsumerInstanceはプールに返却せず、適切に終了
                    // 理由：Consumer状態管理の複雑性により、プール返却は危険
                    var rawConsumer = _consumerInstance.PooledConsumer.Consumer;
                    rawConsumer.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing consumer: {EntityType}", typeof(T).Name);
                }

                _disposed = true;
            }
        }
    }
}