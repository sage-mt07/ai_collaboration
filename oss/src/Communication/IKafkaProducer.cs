using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;

namespace KsqlDsl.Communication;

// =============================================================================
// Core Interfaces - 型安全な Producer/Consumer の定義
// =============================================================================

/// <summary>
/// 型安全Producer インターフェース
/// 設計理由：型安全性確保、テスタビリティ向上
/// 既存Avro実装との統合により高性能なシリアライゼーション実現
/// </summary>
public interface IKafkaProducer<T> : IDisposable where T : class
{
    /// <summary>
    /// 単一メッセージ送信
    /// </summary>
    Task<KafkaDeliveryResult> SendAsync(T message, KafkaMessageContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// バッチメッセージ送信
    /// </summary>
    Task<KafkaBatchDeliveryResult> SendBatchAsync(IEnumerable<T> messages, KafkaMessageContext? context = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 設定・統計情報取得
    /// </summary>
    KafkaProducerStats GetStats();

    /// <summary>
    /// 保留中メッセージのフラッシュ
    /// </summary>
    Task FlushAsync(TimeSpan timeout);

    /// <summary>
    /// トピック名取得
    /// </summary>
    string TopicName { get; }
}

/// <summary>
/// 型安全Consumer インターフェース
/// 設計理由：型安全性確保、購読パターンの統一
/// 既存Avro実装との統合により高性能なデシリアライゼーション実現
/// </summary>
public interface IKafkaConsumer<T> : IDisposable where T : class
{
    /// <summary>
    /// 非同期メッセージストリーム消費
    /// </summary>
    IAsyncEnumerable<KafkaMessage<T>> ConsumeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// バッチ消費
    /// </summary>
    Task<KafkaBatch<T>> ConsumeBatchAsync(KafkaBatchOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// オフセットコミット
    /// </summary>
    Task CommitAsync();

    /// <summary>
    /// オフセットシーク
    /// </summary>
    Task SeekAsync(TopicPartitionOffset offset);

    /// <summary>
    /// 統計・状態情報取得
    /// </summary>
    KafkaConsumerStats GetStats();

    /// <summary>
    /// 割り当てパーティション取得
    /// </summary>
    List<TopicPartition> GetAssignedPartitions();

    /// <summary>
    /// トピック名取得
    /// </summary>
    string TopicName { get; }
}

/// <summary>
/// KafkaMessageBus統合インターフェース
/// 設計理由：アプリケーション層への統一APIの提供
/// </summary>
public interface IKafkaMessageBus : IDisposable
{
    /// <summary>
    /// 単一メッセージ送信
    /// </summary>
    Task SendAsync<T>(T message, KafkaMessageContext? context = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// バッチメッセージ送信
    /// </summary>
    Task SendBatchAsync<T>(IEnumerable<T> messages, KafkaMessageContext? context = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// リアルタイム消費ストリーム
    /// </summary>
    IAsyncEnumerable<T> ConsumeAsync<T>(KafkaSubscriptionOptions? options = null, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// バッチフェッチ（Pull型取得）
    /// </summary>
    Task<List<T>> FetchAsync<T>(KafkaFetchOptions options, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// ヘルスレポート取得
    /// </summary>
    Task<KafkaHealthReport> GetHealthReportAsync();

    /// <summary>
    /// 診断情報取得
    /// </summary>
    KafkaDiagnostics GetDiagnostics();
}

// =============================================================================
// Data Transfer Objects - メッセージ・結果・設定の定義
// =============================================================================

/// <summary>
/// Kafkaメッセージラッパー
/// 設計理由：型安全性とメタデータの統合管理
/// </summary>
public class KafkaMessage<T> where T : class
{
    public T Value { get; set; } = default!;
    public object? Key { get; set; }
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public DateTime Timestamp { get; set; }
    public Headers? Headers { get; set; }
    public KafkaMessageContext? Context { get; set; }
}

/// <summary>
/// メッセージ送信時のコンテキスト情報
/// 設計理由：横断的関心事（トレーシング、パーティション指定等）の管理
/// </summary>
public class KafkaMessageContext
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public int? TargetPartition { get; set; }
    public Dictionary<string, object> Headers { get; set; } = new();
    public TimeSpan? Timeout { get; set; }
    public Dictionary<string, object> Tags { get; set; } = new();

    // OpenTelemetry連携
    public System.Diagnostics.ActivityContext? ActivityContext { get; set; }
}

/// <summary>
/// メッセージ送信結果
/// </summary>
public class KafkaDeliveryResult
{
    public string Topic { get; set; } = string.Empty;
    public int Partition { get; set; }
    public long Offset { get; set; }
    public DateTime Timestamp { get; set; }
    public PersistenceStatus Status { get; set; }
    public Error? Error { get; set; }
    public TimeSpan Latency { get; set; }
}

/// <summary>
/// バッチ送信結果
/// </summary>
public class KafkaBatchDeliveryResult
{
    public string Topic { get; set; } = string.Empty;
    public int TotalMessages { get; set; }
    public int SuccessfulCount { get; set; }
    public int FailedCount { get; set; }
    public bool AllSuccessful => FailedCount == 0;
    public List<KafkaDeliveryResult> Results { get; set; } = new();
    public List<BatchDeliveryError> Errors { get; set; } = new();
    public TimeSpan TotalLatency { get; set; }
}

/// <summary>
/// バッチ内個別エラー
/// </summary>
public class BatchDeliveryError
{
    public int MessageIndex { get; set; }
    public Error Error { get; set; } = default!;
    public object? OriginalMessage { get; set; }
}

/// <summary>
/// バッチコンテナ
/// </summary>
public class KafkaBatch<T> where T : class
{
    public List<KafkaMessage<T>> Messages { get; set; } = new();
    public DateTime BatchStartTime { get; set; }
    public DateTime BatchEndTime { get; set; }
    public TimeSpan ProcessingTime => BatchEndTime - BatchStartTime;
    public int TotalMessages => Messages.Count;
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>
    /// バッチ処理完了時のコミット
    /// </summary>
    public Task CommitAsync() => Task.CompletedTask; // 実装時に適切な処理を追加
}

// =============================================================================
// Configuration Classes - 設定・オプションクラス
// =============================================================================

/// <summary>
/// 購読オプション
/// </summary>
public class KafkaSubscriptionOptions
{
    public string? GroupId { get; set; }
    public bool AutoCommit { get; set; } = true;
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;
    public bool EnablePartitionEof { get; set; } = false;
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(3);
    public bool StopOnError { get; set; } = false;
    public int MaxPollRecords { get; set; } = 500;
    public TimeSpan MaxPollInterval { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// フェッチオプション
/// </summary>
public class KafkaFetchOptions
{
    public string? ConsumerGroupId { get; set; }
    public int MaxMessages { get; set; } = 1000;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    public TopicPartitionOffset? FromOffset { get; set; }
    public TopicPartitionOffset? ToOffset { get; set; }
    public List<TopicPartition>? SpecificPartitions { get; set; }
}

/// <summary>
/// バッチオプション
/// </summary>
public class KafkaBatchOptions
{
    public int MaxBatchSize { get; set; } = 100;
    public TimeSpan MaxWaitTime { get; set; } = TimeSpan.FromSeconds(5);
    public bool AutoCommit { get; set; } = true;
    public bool EnableEmptyBatches { get; set; } = false;
    public string? ConsumerGroupId { get; set; }
}

// =============================================================================
// Statistics & Health Classes - 統計・ヘルス情報
// =============================================================================

/// <summary>
/// Producer統計情報
/// </summary>
public class KafkaProducerStats
{
    public long TotalMessagesSent { get; set; }
    public long SuccessfulMessages { get; set; }
    public long FailedMessages { get; set; }
    public double SuccessRate => TotalMessagesSent > 0 ? (double)SuccessfulMessages / TotalMessagesSent : 0;
    public TimeSpan AverageLatency { get; set; }
    public TimeSpan MinLatency { get; set; }
    public TimeSpan MaxLatency { get; set; }
    public DateTime LastMessageSent { get; set; }
    public long TotalBytesSent { get; set; }
    public double MessagesPerSecond { get; set; }
}

/// <summary>
/// Consumer統計情報
/// </summary>
public class KafkaConsumerStats
{
    public long TotalMessagesReceived { get; set; }
    public long ProcessedMessages { get; set; }
    public long FailedMessages { get; set; }
    public double SuccessRate => TotalMessagesReceived > 0 ? (double)ProcessedMessages / TotalMessagesReceived : 0;
    public TimeSpan AverageProcessingTime { get; set; }
    public TimeSpan MinProcessingTime { get; set; }
    public TimeSpan MaxProcessingTime { get; set; }
    public DateTime LastMessageReceived { get; set; }
    public long TotalBytesReceived { get; set; }
    public double MessagesPerSecond { get; set; }
    public Dictionary<TopicPartition, long> ConsumerLag { get; set; } = new();
    public List<TopicPartition> AssignedPartitions { get; set; } = new();
}

// =============================================================================
// Key Classes - キー・識別子クラス
// =============================================================================

/// <summary>
/// Producer識別キー
/// 設計理由：プール管理での効率的なProducer識別
/// </summary>
public class ProducerKey : IEquatable<ProducerKey>
{
    public Type EntityType { get; }
    public string TopicName { get; }
    public int ConfigurationHash { get; }

    public ProducerKey(Type entityType, string topicName, int configurationHash)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        ConfigurationHash = configurationHash;
    }

    public override int GetHashCode() => HashCode.Combine(EntityType, TopicName, ConfigurationHash);

    public bool Equals(ProducerKey? other)
    {
        return other != null &&
               EntityType == other.EntityType &&
               TopicName == other.TopicName &&
               ConfigurationHash == other.ConfigurationHash;
    }

    public override bool Equals(object? obj) => obj is ProducerKey other && Equals(other);

    public override string ToString() => $"{EntityType.Name}:{TopicName}:{ConfigurationHash:X8}";
}

/// <summary>
/// Consumer識別キー
/// 設計理由：プール管理での効率的なConsumer識別
/// </summary>
public class ConsumerKey : IEquatable<ConsumerKey>
{
    public Type EntityType { get; }
    public string TopicName { get; }
    public string GroupId { get; }

    public ConsumerKey(Type entityType, string topicName, string groupId)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        GroupId = groupId ?? throw new ArgumentNullException(nameof(groupId));
    }

    public override int GetHashCode() => HashCode.Combine(EntityType, TopicName, GroupId);

    public bool Equals(ConsumerKey? other)
    {
        return other != null &&
               EntityType == other.EntityType &&
               TopicName == other.TopicName &&
               GroupId == other.GroupId;
    }

    public override bool Equals(object? obj) => obj is ConsumerKey other && Equals(other);

    public override string ToString() => $"{EntityType.Name}:{TopicName}:{GroupId}";
}

// =============================================================================
// Pool & Internal Classes - プール・内部管理クラス
// =============================================================================

/// <summary>
/// プールされたProducer
/// </summary>
public class PooledProducer
{
    public IProducer<object, object> Producer { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public int UsageCount { get; set; }
    public bool IsHealthy { get; set; } = true;
}

/// <summary>
/// プールされたConsumer
/// </summary>
public class PooledConsumer
{
    public IConsumer<object, object> Consumer { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUsed { get; set; }
    public int UsageCount { get; set; }
    public bool IsHealthy { get; set; } = true;
    public List<TopicPartition> AssignedPartitions { get; set; } = new();
}

/// <summary>
/// 購読管理情報
/// </summary>
public class ConsumerSubscription
{
    public string Id { get; set; } = string.Empty;
    public Type EntityType { get; set; } = default!;
    public IKafkaConsumer<object> Consumer { get; set; } = default!;
    public Func<object, KafkaMessageContext, Task> Handler { get; set; } = default!;
    public KafkaSubscriptionOptions Options { get; set; } = default!;
    public DateTime StartedAt { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
}

/// <summary>
/// 購読情報（診断用）
/// </summary>
public class SubscriptionInfo
{
    public string Id { get; set; } = string.Empty;
    public Type EntityType { get; set; } = default!;
    public DateTime StartedAt { get; set; }
    public KafkaSubscriptionOptions Options { get; set; } = default!;
}

// =============================================================================
// Exception Classes - 例外クラス
// =============================================================================

/// <summary>
/// KafkaMessageBus基底例外
/// </summary>
public class KafkaMessageBusException : Exception
{
    public KafkaMessageBusException(string message) : base(message) { }
    public KafkaMessageBusException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Producer管理例外
/// </summary>
public class KafkaProducerManagerException : KafkaMessageBusException
{
    public KafkaProducerManagerException(string message) : base(message) { }
    public KafkaProducerManagerException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Consumer管理例外
/// </summary>
public class KafkaConsumerManagerException : KafkaMessageBusException
{
    public KafkaConsumerManagerException(string message) : base(message) { }
    public KafkaConsumerManagerException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// バッチ送信例外
/// </summary>
public class KafkaBatchSendException : KafkaMessageBusException
{
    public KafkaBatchDeliveryResult BatchResult { get; }

    public KafkaBatchSendException(string message, KafkaBatchDeliveryResult batchResult) : base(message)
    {
        BatchResult = batchResult;
    }

    public KafkaBatchSendException(string message, KafkaBatchDeliveryResult batchResult, Exception innerException)
        : base(message, innerException)
    {
        BatchResult = batchResult;
    }
}

/// <summary>
/// Producerプール例外
/// </summary>
public class ProducerPoolException : KafkaMessageBusException
{
    public ProducerPoolException(string message) : base(message) { }
    public ProducerPoolException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Consumerプール例外
/// </summary>
public class ConsumerPoolException : KafkaMessageBusException
{
    public ConsumerPoolException(string message) : base(message) { }
    public ConsumerPoolException(string message, Exception innerException) : base(message, innerException) { }
}

// =============================================================================
// Activity Source & Metrics - 監視・計測
// =============================================================================

/// <summary>
/// Kafka通信用ActivitySource
/// 設計理由：OpenTelemetry分散トレーシングとの統合
/// </summary>
public static class KafkaActivitySource
{
    private static readonly System.Diagnostics.ActivitySource _activitySource =
        new("KsqlDsl.Communication", "1.0.0");

    public static System.Diagnostics.Activity? StartActivity(string name) =>
        _activitySource.StartActivity(name);
}

/// <summary>
/// Kafka通信メトリクス
/// 設計理由：既存AvroMetricsとの統合、標準メトリクス提供
/// </summary>
public static class KafkaMetrics
{
    private static readonly System.Diagnostics.Metrics.Meter _meter =
        new("KsqlDsl.Communication", "1.0.0");

    // カウンター
    private static readonly System.Diagnostics.Metrics.Counter<long> _messagesSent =
        _meter.CreateCounter<long>("kafka_messages_sent_total");
    private static readonly System.Diagnostics.Metrics.Counter<long> _messagesReceived =
        _meter.CreateCounter<long>("kafka_messages_received_total");
    private static readonly System.Diagnostics.Metrics.Counter<long> _batchesSent =
        _meter.CreateCounter<long>("kafka_batches_sent_total");

    // ヒストグラム
    private static readonly System.Diagnostics.Metrics.Histogram<double> _sendLatency =
        _meter.CreateHistogram<double>("kafka_send_latency_ms", "ms");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _processingTime =
        _meter.CreateHistogram<double>("kafka_processing_time_ms", "ms");

    public static void RecordMessageSent(string topic, string entityType, bool success, TimeSpan duration)
    {
        _messagesSent.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("entity_type", entityType),
            new KeyValuePair<string, object?>("success", success));

        _sendLatency.Record(duration.TotalMilliseconds,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("entity_type", entityType));
    }

    public static void RecordBatchSent(string topic, int messageCount, bool success, TimeSpan duration)
    {
        _batchesSent.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("success", success));

        _messagesSent.Add(messageCount,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("batch", true),
            new KeyValuePair<string, object?>("success", success));
    }

    public static void RecordMessageReceived(string topic, string entityType, TimeSpan processingTime)
    {
        _messagesReceived.Add(1,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("entity_type", entityType));

        _processingTime.Record(processingTime.TotalMilliseconds,
            new KeyValuePair<string, object?>("topic", topic),
            new KeyValuePair<string, object?>("entity_type", entityType));
    }

    public static void RecordThroughput(string direction, string topic, long bytesPerSecond)
    {
        // 実装では適切なメトリクスを記録
    }

    public static void RecordSerializationError(string entityType, string errorType)
    {
        // 実装では適切なメトリクスを記録
    }

    public static void RecordConnectionError(string brokerHost, string errorType)
    {
        // 実装では適切なメトリクスを記録
    }
}