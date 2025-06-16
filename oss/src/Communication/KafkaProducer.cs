using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using KsqlDsl.Modeling;
using Microsoft.Extensions.Logging;

namespace KsqlDsl.Communication;

// =============================================================================
// KafkaProducer Implementation - 型安全Producer実装
// =============================================================================

/// <summary>
/// 型安全Producer実装
/// 設計理由：型安全性確保、既存Avroシリアライザーとの統合
/// ProducerManagerから管理される具象実装クラス
/// </summary>
public class KafkaProducer<T> : IKafkaProducer<T> where T : class
{
    private readonly IProducer<object, object> _rawProducer;
    private readonly ISerializer<object> _keySerializer;
    private readonly ISerializer<object> _valueSerializer;
    private readonly EntityModel _entityModel;
    private readonly KafkaProducerManager _manager;
    private readonly ILogger _logger;
    private readonly KafkaProducerStats _stats = new();
    private bool _disposed = false;

    public string TopicName { get; }

    // 内部プロパティ（ProducerManagerからのアクセス用）
    internal IProducer<object, object> RawProducer => _rawProducer;

    public KafkaProducer(
        IProducer<object, object> rawProducer,
        ISerializer<object> keySerializer,
        ISerializer<object> valueSerializer,
        string topicName,
        EntityModel entityModel,
        KafkaProducerManager manager,
        ILogger logger)
    {
        _rawProducer = rawProducer ?? throw new ArgumentNullException(nameof(rawProducer));
        _keySerializer = keySerializer ?? throw new ArgumentNullException(nameof(keySerializer));
        _valueSerializer = valueSerializer ?? throw new ArgumentNullException(nameof(valueSerializer));
        TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 単一メッセージ送信
    /// 設計理由：型安全性とパフォーマンス監視を両立
    /// </summary>
    public async Task<KafkaDeliveryResult> SendAsync(T message, KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // キー抽出（既存KeyExtractor活用）
            var keyValue = ExtractKeyValue(message);

            // メッセージ構築
            var kafkaMessage = new Message<object, object>
            {
                Key = keyValue,
                Value = message,
                Headers = BuildHeaders(context),
                Timestamp = new Timestamp(DateTime.UtcNow)
            };

            // パーティション指定
            var topicPartition = context?.TargetPartition.HasValue == true
                ? new TopicPartition(TopicName, new Partition(context.TargetPartition.Value))
                : new TopicPartition(TopicName, Partition.Any);

            // 送信実行
            var deliveryResult = await _rawProducer.ProduceAsync(topicPartition, kafkaMessage, cancellationToken);
            stopwatch.Stop();

            // 統計更新
            UpdateSendStats(success: true, stopwatch.Elapsed);

            // 結果構築
            var result = new KafkaDeliveryResult
            {
                Topic = deliveryResult.Topic,
                Partition = deliveryResult.Partition.Value,
                Offset = deliveryResult.Offset.Value,
                Timestamp = deliveryResult.Timestamp.UtcDateTime,
                Status = deliveryResult.Status,
                Latency = stopwatch.Elapsed
            };

            _logger.LogTrace("Message sent: {EntityType} -> {Topic}:{Partition}:{Offset}",
                typeof(T).Name, result.Topic, result.Partition, result.Offset);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdateSendStats(success: false, stopwatch.Elapsed);

            _logger.LogError(ex, "Failed to send message: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            throw;
        }
    }

    /// <summary>
    /// バッチメッセージ送信
    /// 設計理由：高スループット対応、トランザクション境界明確化
    /// </summary>
    public async Task<KafkaBatchDeliveryResult> SendBatchAsync(IEnumerable<T> messages, KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
    {
        if (messages == null)
            throw new ArgumentNullException(nameof(messages));

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            return new KafkaBatchDeliveryResult
            {
                Topic = TopicName,
                AllSuccessful = true
            };
        }

        var stopwatch = Stopwatch.StartNew();
        var results = new List<KafkaDeliveryResult>();
        var errors = new List<BatchDeliveryError>();

        try
        {
            var tasks = new List<Task<DeliveryResult<object, object>>>();

            // 並列送信タスク構築
            for (int i = 0; i < messageList.Count; i++)
            {
                var message = messageList[i];
                var keyValue = ExtractKeyValue(message);

                var kafkaMessage = new Message<object, object>
                {
                    Key = keyValue,
                    Value = message,
                    Headers = BuildHeaders(context),
                    Timestamp = new Timestamp(DateTime.UtcNow)
                };

                var task = _rawProducer.ProduceAsync(TopicName, kafkaMessage, cancellationToken);
                tasks.Add(task);
            }

            // 全タスク完了待機
            var deliveryResults = await Task.WhenAll(tasks);
            stopwatch.Stop();

            // 結果集計
            for (int i = 0; i < deliveryResults.Length; i++)
            {
                var deliveryResult = deliveryResults[i];

                if (deliveryResult.Error.IsError)
                {
                    errors.Add(new BatchDeliveryError
                    {
                        MessageIndex = i,
                        Error = deliveryResult.Error,
                        OriginalMessage = messageList[i]
                    });
                }
                else
                {
                    results.Add(new KafkaDeliveryResult
                    {
                        Topic = deliveryResult.Topic,
                        Partition = deliveryResult.Partition.Value,
                        Offset = deliveryResult.Offset.Value,
                        Timestamp = deliveryResult.Timestamp.UtcDateTime,
                        Status = deliveryResult.Status,
                        Latency = stopwatch.Elapsed
                    });
                }
            }

            // バッチ統計更新
            UpdateBatchSendStats(messageList.Count, errors.Count == 0, stopwatch.Elapsed);

            var batchResult = new KafkaBatchDeliveryResult
            {
                Topic = TopicName,
                TotalMessages = messageList.Count,
                SuccessfulCount = results.Count,
                FailedCount = errors.Count,
                Results = results,
                Errors = errors,
                TotalLatency = stopwatch.Elapsed
            };

            _logger.LogDebug("Batch sent: {EntityType} -> {Topic}, {SuccessCount}/{TotalCount} successful",
                typeof(T).Name, TopicName, results.Count, messageList.Count);

            return batchResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdateBatchSendStats(messageList.Count, success: false, stopwatch.Elapsed);

            _logger.LogError(ex, "Failed to send batch: {EntityType} -> {Topic}, {MessageCount} messages",
                typeof(T).Name, TopicName, messageList.Count);
            throw;
        }
    }

    /// <summary>
    /// 統計情報取得
    /// </summary>
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

    /// <summary>
    /// 保留メッセージフラッシュ
    /// </summary>
    public async Task FlushAsync(TimeSpan timeout)
    {
        try
        {
            _rawProducer.Flush(timeout);
            await Task.Delay(1); // 非同期メソッドの形式保持

            _logger.LogTrace("Producer flushed: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush producer: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            throw;
        }
    }

    // プライベートヘルパーメソッド

    private object? ExtractKeyValue(T message)
    {
        // 既存KeyExtractor.ExtractKeyを活用
        try
        {
            return KsqlDsl.Avro.KeyExtractor.ExtractKey(message, _entityModel);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract key from message: {EntityType}", typeof(T).Name);
            return null;
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

    private void UpdateSendStats(bool success, TimeSpan latency)
    {
        lock (_stats)
        {
            _stats.TotalMessagesSent++;

            if (success)
            {
                _stats.SuccessfulMessages++;
            }
            else
            {
                _stats.FailedMessages++;
            }

            // レイテンシ統計更新
            if (_stats.MinLatency == TimeSpan.Zero || latency < _stats.MinLatency)
                _stats.MinLatency = latency;
            if (latency > _stats.MaxLatency)
                _stats.MaxLatency = latency;

            // 平均レイテンシ計算（単純移動平均）
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

    private void UpdateBatchSendStats(int messageCount, bool success, TimeSpan latency)
    {
        lock (_stats)
        {
            _stats.TotalMessagesSent += messageCount;

            if (success)
            {
                _stats.SuccessfulMessages += messageCount;
            }
            else
            {
                _stats.FailedMessages += messageCount;
            }

            _stats.LastMessageSent = DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                // Producerの返却はManagerに委譲
                _manager.ReturnProducer(this);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error returning producer to manager: {EntityType}", typeof(T).Name);
            }

            _disposed = true;
        }
    }
}

// =============================================================================
// KafkaConsumer Implementation - 型安全Consumer実装
// =============================================================================

/// <summary>
/// 型安全Consumer実装
/// 設計理由：型安全性確保、既存Avroデシリアライザーとの統合
/// ConsumerManagerから管理される具象実装クラス
/// </summary>
public class KafkaConsumer<T> : IKafkaConsumer<T> where T : class
{
    private readonly IConsumer<object, object> _rawConsumer;
    private readonly IDeserializer<object> _keyDeserializer;
    private readonly IDeserializer<object> _valueDeserializer;
    private readonly EntityModel _entityModel;
    private readonly KafkaSubscriptionOptions _options;
    private readonly KafkaConsumerManager _manager;
    private readonly ILogger _logger;
    private readonly KafkaConsumerStats _stats = new();
    private bool _subscribed = false;
    private bool _disposed = false;

    public string TopicName { get; }

    public KafkaConsumer(
        IConsumer<object, object> rawConsumer,
        IDeserializer<object> keyDeserializer,
        IDeserializer<object> valueDeserializer,
        string topicName,
        EntityModel entityModel,
        KafkaSubscriptionOptions options,
        KafkaConsumerManager manager,
        ILogger logger)
    {
        _rawConsumer = rawConsumer ?? throw new ArgumentNullException(nameof(rawConsumer));
        _keyDeserializer = keyDeserializer ?? throw new ArgumentNullException(nameof(keyDeserializer));
        _valueDeserializer = valueDeserializer ?? throw new ArgumentNullException(nameof(valueDeserializer));
        TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 購読開始
        EnsureSubscribed();
    }

    /// <summary>
    /// 非同期メッセージストリーム消費
    /// 設計理由：標準的な非同期ストリーム処理パターン
    /// </summary>
    public async IAsyncEnumerable<KafkaMessage<T>> ConsumeAsync(CancellationToken cancellationToken = default)
    {
        EnsureSubscribed();

        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<object, object>? consumeResult = null;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                // メッセージ消費（タイムアウト付き）
                consumeResult = _rawConsumer.Consume(TimeSpan.FromSeconds(1));

                if (consumeResult == null)
                {
                    // タイムアウト時は継続
                    continue;
                }

                if (consumeResult.IsPartitionEOF)
                {
                    // パーティション終端の場合は継続
                    continue;
                }

                stopwatch.Stop();

                // デシリアライゼーション
                var message = await DeserializeMessageAsync(consumeResult);

                // 統計更新
                UpdateConsumeStats(success: true, stopwatch.Elapsed);

                yield return message;
            }
            catch (ConsumeException ex)
            {
                stopwatch.Stop();
                UpdateConsumeStats(success: false, stopwatch.Elapsed);

                _logger.LogError(ex, "Consume error: {EntityType} -> {Topic}", typeof(T).Name, TopicName);

                // 致命的でないエラーは継続
                if (ex.Error.IsFatal)
                {
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                // キャンセル時は正常終了
                yield break;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                UpdateConsumeStats(success: false, stopwatch.Elapsed);

                _logger.LogError(ex, "Unexpected consume error: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
                throw;
            }
        }
    }

    /// <summary>
    /// バッチ消費
    /// 設計理由：高スループット処理対応
    /// </summary>
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

            while (messages.Count < options.MaxBatchSize &&
                   DateTime.UtcNow < endTime &&
                   !cancellationToken.IsCancellationRequested)
            {
                var remainingTime = endTime - DateTime.UtcNow;
                if (remainingTime <= TimeSpan.Zero) break;

                var consumeResult = _rawConsumer.Consume(remainingTime);

                if (consumeResult == null)
                {
                    // タイムアウト
                    break;
                }

                if (consumeResult.IsPartitionEOF)
                {
                    // パーティション終端
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
                    _logger.LogWarning(ex, "Failed to deserialize message in batch: {EntityType}", typeof(T).Name);
                    // バッチ処理では個別エラーは継続
                }
            }

            batchStopwatch.Stop();
            batch.BatchEndTime = DateTime.UtcNow;
            batch.Messages = messages;

            // バッチ統計更新
            UpdateBatchConsumeStats(messages.Count, batchStopwatch.Elapsed);

            _logger.LogTrace("Batch consumed: {EntityType} -> {Topic}, {MessageCount} messages",
                typeof(T).Name, TopicName, messages.Count);

            return batch;
        }
        catch (Exception ex)
        {
            batchStopwatch.Stop();
            batch.BatchEndTime = DateTime.UtcNow;

            _logger.LogError(ex, "Failed to consume batch: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            throw;
        }
    }

    /// <summary>
    /// オフセットコミット
    /// </summary>
    public async Task CommitAsync()
    {
        try
        {
            _rawConsumer.Commit();
            await Task.Delay(1); // 非同期メソッドの形式保持

            _logger.LogTrace("Offset committed: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit offset: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            throw;
        }
    }

    /// <summary>
    /// オフセットシーク
    /// </summary>
    public async Task SeekAsync(TopicPartitionOffset offset)
    {
        if (offset == null)
            throw new ArgumentNullException(nameof(offset));

        try
        {
            _rawConsumer.Seek(offset);
            await Task.Delay(1); // 非同期メソッドの形式保持

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

    /// <summary>
    /// 統計情報取得
    /// </summary>
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

    /// <summary>
    /// 割り当てパーティション取得
    /// </summary>
    public List<TopicPartition> GetAssignedPartitions()
    {
        try
        {
            var assignment = _rawConsumer.Assignment;
            return assignment?.ToList() ?? new List<TopicPartition>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get assigned partitions: {EntityType}", typeof(T).Name);
            return new List<TopicPartition>();
        }
    }

    // プライベートヘルパーメソッド

    private void EnsureSubscribed()
    {
        if (!_subscribed)
        {
            try
            {
                _rawConsumer.Subscribe(TopicName);
                _subscribed = true;

                _logger.LogDebug("Subscribed to topic: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to subscribe to topic: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
                throw;
            }
        }
    }

    private async Task<KafkaMessage<T>> DeserializeMessageAsync(ConsumeResult<object, object> consumeResult)
    {
        await Task.Delay(1); // 非同期メソッドの形式保持

        try
        {
            // 値のデシリアライゼーション
            T value;
            if (consumeResult.Message.Value != null)
            {
                var deserializedValue = _valueDeserializer.Deserialize(
                    ReadOnlySpan<byte>.Empty, // 実際の実装では適切なデータを渡す
                    false,
                    new SerializationContext(MessageComponentType.Value, TopicName));

                value = (T)deserializedValue;
            }
            else
            {
                throw new InvalidOperationException("Message value cannot be null");
            }

            // キーのデシリアライゼーション
            object? key = null;
            if (consumeResult.Message.Key != null)
            {
                key = _keyDeserializer.Deserialize(
                    ReadOnlySpan<byte>.Empty, // 実際の実装では適切なデータを渡す
                    false,
                    new SerializationContext(MessageComponentType.Key, TopicName));
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
            _logger.LogError(ex, "Failed to deserialize message: {EntityType} -> {Topic}", typeof(T).Name, TopicName);
            throw;
        }
    }

    private void UpdateConsumeStats(bool success, TimeSpan processingTime)
    {
        lock (_stats)
        {
            _stats.TotalMessagesReceived++;

            if (success)
            {
                _stats.ProcessedMessages++;
            }
            else
            {
                _stats.FailedMessages++;
            }

            // 処理時間統計更新
            if (_stats.MinProcessingTime == TimeSpan.Zero || processingTime < _stats.MinProcessingTime)
                _stats.MinProcessingTime = processingTime;
            if (processingTime > _stats.MaxProcessingTime)
                _stats.MaxProcessingTime = processingTime;

            // 平均処理時間計算
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
                    _rawConsumer.Unsubscribe();
                    _subscribed = false;
                }

                // Consumerはプールに返却しない（状態管理が複雑なため）
                // 代わりにCloseで適切に終了
                _rawConsumer.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing consumer: {EntityType}", typeof(T).Name);
            }

            _disposed = true;
        }
    }
}