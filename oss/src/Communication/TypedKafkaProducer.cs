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
    /// 型安全Producer実装（既存Avroシリアライザー統合版）
    /// 設計理由：既存の95%実装済みEnhancedAvroSerializerManagerを活用し、
    /// 型安全性とパフォーマンス監視を両立
    /// </summary>
    internal class TypedKafkaProducer<T> : IKafkaProducer<T> where T : class
    {
        private readonly IProducer<object, object> _producer;
        private readonly ISerializer<object> _keySerializer;
        private readonly ISerializer<object> _valueSerializer;
        private readonly string _topicName;
        private readonly EntityModel _entityModel;
        private readonly ILogger _logger;
        private readonly KafkaProducerStats _stats = new();
        private bool _disposed = false;

        public string TopicName => _topicName;

        public TypedKafkaProducer(
            IProducer<object, object> producer,
            ISerializer<object> keySerializer,
            ISerializer<object> valueSerializer,
            string topicName,
            EntityModel entityModel,
            ILogger logger)
        {
            _producer = producer ?? throw new ArgumentNullException(nameof(producer));
            _keySerializer = keySerializer ?? throw new ArgumentNullException(nameof(keySerializer));
            _valueSerializer = valueSerializer ?? throw new ArgumentNullException(nameof(valueSerializer));
            _topicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
            _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<KafkaDeliveryResult> SendAsync(T message, KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // 既存KeyExtractorを活用してキー抽出
                var keyValue = KeyExtractor.ExtractKey(message, _entityModel);

                var kafkaMessage = new Message<object, object>
                {
                    Key = keyValue,
                    Value = message,
                    Headers = BuildHeaders(context),
                    Timestamp = new Timestamp(DateTime.UtcNow)
                };

                var topicPartition = context?.TargetPartition.HasValue == true
                    ? new TopicPartition(_topicName, new Partition(context.TargetPartition.Value))
                    : new TopicPartition(_topicName, Partition.Any);

                var deliveryResult = await _producer.ProduceAsync(topicPartition, kafkaMessage, cancellationToken);
                stopwatch.Stop();

                UpdateStats(success: true, stopwatch.Elapsed);

                // 既存KafkaMetricsと統合
                KafkaMetrics.RecordMessageSent(_topicName, typeof(T).Name, true, stopwatch.Elapsed);

                return new KafkaDeliveryResult
                {
                    Topic = deliveryResult.Topic,
                    Partition = deliveryResult.Partition.Value,
                    Offset = deliveryResult.Offset.Value,
                    Timestamp = deliveryResult.Timestamp.UtcDateTime,
                    Status = deliveryResult.Status,
                    Latency = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateStats(success: false, stopwatch.Elapsed);
                KafkaMetrics.RecordMessageSent(_topicName, typeof(T).Name, false, stopwatch.Elapsed);

                _logger.LogError(ex, "Failed to send message: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                throw;
            }
        }

        public async Task<KafkaBatchDeliveryResult> SendBatchAsync(IEnumerable<T> messages, KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            var messageList = messages.ToList();
            if (messageList.Count == 0)
            {
                return new KafkaBatchDeliveryResult
                {
                    Topic = _topicName,
                    AllSuccessful = true
                };
            }

            var stopwatch = Stopwatch.StartNew();
            var results = new List<KafkaDeliveryResult>();
            var errors = new List<BatchDeliveryError>();

            try
            {
                var tasks = messageList.Select(async (message, index) =>
                {
                    try
                    {
                        var keyValue = KeyExtractor.ExtractKey(message, _entityModel);
                        var kafkaMessage = new Message<object, object>
                        {
                            Key = keyValue,
                            Value = message,
                            Headers = BuildHeaders(context),
                            Timestamp = new Timestamp(DateTime.UtcNow)
                        };

                        var deliveryResult = await _producer.ProduceAsync(_topicName, kafkaMessage, cancellationToken);

                        return new { Index = index, Result = deliveryResult, Error = (Error?)null };
                    }
                    catch (ProduceException<object, object> ex)
                    {
                        return new { Index = index, Result = (DeliveryResult<object, object>?)null, Error = ex.Error };
                    }
                });

                var taskResults = await Task.WhenAll(tasks);
                stopwatch.Stop();

                foreach (var taskResult in taskResults)
                {
                    if (taskResult.Error != null)
                    {
                        errors.Add(new BatchDeliveryError
                        {
                            MessageIndex = taskResult.Index,
                            Error = taskResult.Error,
                            OriginalMessage = messageList[taskResult.Index]
                        });
                    }
                    else if (taskResult.Result != null)
                    {
                        results.Add(new KafkaDeliveryResult
                        {
                            Topic = taskResult.Result.Topic,
                            Partition = taskResult.Result.Partition.Value,
                            Offset = taskResult.Result.Offset.Value,
                            Timestamp = taskResult.Result.Timestamp.UtcDateTime,
                            Status = taskResult.Result.Status,
                            Latency = stopwatch.Elapsed
                        });
                    }
                }

                var batchResult = new KafkaBatchDeliveryResult
                {
                    Topic = _topicName,
                    TotalMessages = messageList.Count,
                    SuccessfulCount = results.Count,
                    FailedCount = errors.Count,
                    Results = results,
                    Errors = errors,
                    TotalLatency = stopwatch.Elapsed
                };

                // 既存KafkaMetricsとの統合
                KafkaMetrics.RecordBatchSent(_topicName, messageList.Count, batchResult.AllSuccessful, stopwatch.Elapsed);

                return batchResult;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                KafkaMetrics.RecordBatchSent(_topicName, messageList.Count, false, stopwatch.Elapsed);

                _logger.LogError(ex, "Failed to send batch: {EntityType} -> {Topic}, {MessageCount} messages",
                    typeof(T).Name, _topicName, messageList.Count);
                throw;
            }
        }

        public KafkaProducerStats GetStats()
        {
            lock (_stats)
            {
                return new KafkaProducerStats
                {
                    TotalMessagesSent = _stats.TotalMessagesSent,
                    SuccessfulMessages = _stats.SuccessfulMessages,
                    FailedMessages = _stats.FailedMessages,
                    AverageLatency = _stats.AverageLatency,
                    MinLatency = _stats.MinLatency,
                    MaxLatency = _stats.MaxLatency,
                    LastMessageSent = _stats.LastMessageSent,
                    TotalBytesSent = _stats.TotalBytesSent,
                    MessagesPerSecond = _stats.MessagesPerSecond
                };
            }
        }

        public async Task FlushAsync(TimeSpan timeout)
        {
            try
            {
                _producer.Flush(timeout);
                await Task.Delay(1);
                _logger.LogTrace("Producer flushed: {EntityType} -> {Topic}", typeof(T).Name, _topicName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to flush producer: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                throw;
            }
        }

        private Headers? BuildHeaders(KafkaMessageContext? context)
        {
            if (context?.Headers == null || !context.Headers.Any())
                return null;

            var headers = new Headers();
            foreach (var kvp in context.Headers)
            {
                if (kvp.Value != null)
                {
                    var valueBytes = System.Text.Encoding.UTF8.GetBytes(kvp.Value.ToString() ?? "");
                    headers.Add(kvp.Key, valueBytes);
                }
            }

            return headers;
        }

        private void UpdateStats(bool success, TimeSpan latency)
        {
            lock (_stats)
            {
                _stats.TotalMessagesSent++;

                if (success)
                    _stats.SuccessfulMessages++;
                else
                    _stats.FailedMessages++;

                if (_stats.MinLatency == TimeSpan.Zero || latency < _stats.MinLatency)
                    _stats.MinLatency = latency;
                if (latency > _stats.MaxLatency)
                    _stats.MaxLatency = latency;

                if (_stats.TotalMessagesSent == 1)
                {
                    _stats.AverageLatency = latency;
                }
                else
                {
                    var totalMs = _stats.AverageLatency.TotalMilliseconds * (_stats.TotalMessagesSent - 1) + latency.TotalMilliseconds;
                    _stats.AverageLatency = TimeSpan.FromMilliseconds(totalMs / _stats.TotalMessagesSent);
                }

                _stats.LastMessageSent = DateTime.UtcNow;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Producer自体の破棄は管理者に委譲
                // 統計の最終出力のみ実行
                _logger.LogDebug("TypedKafkaProducer disposed: {EntityType} -> {Topic}",
                    typeof(T).Name, _topicName);
                _disposed = true;
            }
        }
    }
}