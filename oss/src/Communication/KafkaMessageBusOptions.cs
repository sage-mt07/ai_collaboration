using Confluent.Kafka;
using KsqlDsl.Avro;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KsqlDsl.Communication;

// =============================================================================
// Configuration Classes - 設定クラス群
// =============================================================================

/// <summary>
/// MessageBus全体設定
/// 設計理由：統合設定による一元管理、既存Avro設定との統合
/// </summary>
public class KafkaMessageBusOptions
{
    // 接続設定
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string? SchemaRegistryUrl { get; set; }

    // Producer設定
    public KafkaProducerConfig ProducerConfig { get; set; } = new();
    public ProducerPoolConfig ProducerPoolConfig { get; set; } = new();

    // Consumer設定
    public KafkaConsumerConfig ConsumerConfig { get; set; } = new();
    public ConsumerPoolConfig ConsumerPoolConfig { get; set; } = new();

    // Avro設定（既存活用）
    public AvroOperationRetrySettings AvroRetrySettings { get; set; } = new();
    public PerformanceThresholds PerformanceThresholds { get; set; } = new();

    // 監視・診断
    public bool EnableDetailedMetrics { get; set; } = true;
    public bool EnableDistributedTracing { get; set; } = true;
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableDebugLogging { get; set; } = false;
}

/// <summary>
/// Producer設定
/// </summary>
public class KafkaProducerConfig
{
    // 基本設定
    public string BootstrapServers { get; set; } = "localhost:9092";
    public Acks Acks { get; set; } = Acks.All;
    public bool EnableIdempotence { get; set; } = true;
    public int MaxInFlight { get; set; } = 1;
    public CompressionType CompressionType { get; set; } = CompressionType.Snappy;

    // パフォーマンス設定
    public int LingerMs { get; set; } = 5;
    public int BatchSize { get; set; } = 16384;
    public int RequestTimeoutMs { get; set; } = 30000;
    public int DeliveryTimeoutMs { get; set; } = 120000;
    public int RetryBackoffMs { get; set; } = 100;

    // セキュリティ設定
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;
    public string? SaslMechanism { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    // 追加設定
    public Dictionary<string, object> AdditionalConfig { get; set; } = new();

    // ヘルス閾値
    public ProducerHealthThresholds HealthThresholds { get; set; } = new();

    public int GetKeyHash()
    {
        return HashCode.Combine(
            BootstrapServers, Acks, EnableIdempotence, MaxInFlight,
            CompressionType, LingerMs, BatchSize);
    }
}

/// <summary>
/// Consumer設定
/// </summary>
public class KafkaConsumerConfig
{
    // 基本設定
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string DefaultGroupId { get; set; } = "ksqldsl-default";
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;
    public bool EnableAutoCommit { get; set; } = true;
    public int AutoCommitIntervalMs { get; set; } = 5000;

    // セッション設定
    public int SessionTimeoutMs { get; set; } = 30000;
    public int HeartbeatIntervalMs { get; set; } = 3000;
    public int MaxPollIntervalMs { get; set; } = 300000;
    public int MaxPollRecords { get; set; } = 500;

    // フェッチ設定
    public int FetchMinBytes { get; set; } = 1;
    public int FetchMaxWaitMs { get; set; } = 500;
    public int FetchMaxBytes { get; set; } = 52428800; // 50MB

    // セキュリティ設定
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;
    public string? SaslMechanism { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }

    // 追加設定
    public Dictionary<string, object> AdditionalConfig { get; set; } = new();

    // ヘルス閾値
    public ConsumerHealthThresholds HealthThresholds { get; set; } = new();

    public int GetKeyHash()
    {
        return HashCode.Combine(
            BootstrapServers, DefaultGroupId, AutoOffsetReset, EnableAutoCommit,
            SessionTimeoutMs, MaxPollRecords);
    }
}

/// <summary>
/// Producerプール設定
/// </summary>
public class ProducerPoolConfig
{
    public int MinPoolSize { get; set; } = 1;
    public int MaxPoolSize { get; set; } = 10;
    public TimeSpan ProducerIdleTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableDynamicScaling { get; set; } = true;
    public double ScaleUpThreshold { get; set; } = 0.8;
    public double ScaleDownThreshold { get; set; } = 0.3;
}

/// <summary>
/// Consumerプール設定
/// </summary>
public class ConsumerPoolConfig
{
    public int MinPoolSize { get; set; } = 1;
    public int MaxPoolSize { get; set; } = 10;
    public TimeSpan ConsumerIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaintenanceInterval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableRebalanceOptimization { get; set; } = true;
    public TimeSpan RebalanceTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

// =============================================================================
// Health Threshold Classes - ヘルス閾値設定
// =============================================================================

/// <summary>
/// Producer健全性閾値
/// </summary>
public class ProducerHealthThresholds
{
    public long MaxAverageLatencyMs { get; set; } = 100;
    public long CriticalLatencyMs { get; set; } = 1000;
    public double MaxFailureRate { get; set; } = 0.1;
    public double CriticalFailureRate { get; set; } = 0.2;
    public long MinThroughputPerSecond { get; set; } = 10;
}

/// <summary>
/// Consumer健全性閾値
/// </summary>
public class ConsumerHealthThresholds
{
    public long MaxAverageProcessingTimeMs { get; set; } = 500;
    public long CriticalProcessingTimeMs { get; set; } = 5000;
    public double MaxFailureRate { get; set; } = 0.1;
    public double CriticalFailureRate { get; set; } = 0.2;
    public long MaxConsumerLag { get; set; } = 10000;
    public long CriticalConsumerLag { get; set; } = 100000;
}

// =============================================================================
// Performance Statistics Classes - パフォーマンス統計
// =============================================================================

/// <summary>
/// Producer全体パフォーマンス統計
/// </summary>
public class ProducerPerformanceStats
{
    public long TotalMessages { get; set; }
    public long TotalBatches { get; set; }
    public long SuccessfulMessages { get; set; }
    public long FailedMessages { get; set; }
    public double FailureRate => TotalMessages > 0 ? (double)FailedMessages / TotalMessages : 0;
    public TimeSpan AverageLatency { get; set; }
    public double ThroughputPerSecond { get; set; }
    public int ActiveProducers { get; set; }
    public Dictionary<Type, ProducerEntityStats> EntityStats { get; set; } = new();
    public DateTime LastUpdated { get; set; }

    // 内部統計フィールド
    public long TotalProducersCreated;
    public long ProducerCreationFailures;
    public DateTime LastThroughputCalculation;
}

/// <summary>
/// Consumer全体パフォーマンス統計
/// </summary>
public class ConsumerPerformanceStats
{
    public long TotalMessages { get; set; }
    public long TotalBatches { get; set; }
    public long ProcessedMessages { get; set; }
    public long FailedMessages { get; set; }
    public double FailureRate => TotalMessages > 0 ? (double)FailedMessages / TotalMessages : 0;
    public TimeSpan AverageProcessingTime { get; set; }
    public double ThroughputPerSecond { get; set; }
    public int ActiveConsumers { get; set; }
    public int ActiveSubscriptions { get; set; }
    public Dictionary<Type, ConsumerEntityStats> EntityStats { get; set; } = new();
    public DateTime LastUpdated { get; set; }

    // 内部統計フィールド
    public long TotalConsumersCreated;
    public long ConsumerCreationFailures;
    public DateTime LastThroughputCalculation;
}

/// <summary>
/// Producer エンティティ別統計
/// </summary>
public class ProducerEntityStats
{
    public Type EntityType { get; set; } = default!;
    public long TotalMessages { get; set; }
    public long TotalBatches { get; set; }
    public long SuccessfulMessages { get; set; }
    public long FailedMessages { get; set; }
    public long SuccessfulBatches { get; set; }
    public long FailedBatches { get; set; }
    public TimeSpan TotalSendTime { get; set; }
    public TimeSpan AverageSendTime { get; set; }
    public long ProducersCreated { get; set; }
    public long CreationFailures { get; set; }
    public TimeSpan TotalCreationTime { get; set; }
    public TimeSpan AverageCreationTime { get; set; }
    public DateTime LastActivity { get; set; }
    public DateTime LastFailure { get; set; }
    public string? LastFailureReason { get; set; }
}

/// <summary>
/// Consumer エンティティ別統計
/// </summary>
public class ConsumerEntityStats
{
    public Type EntityType { get; set; } = default!;
    public long TotalMessages { get; set; }
    public long TotalBatches { get; set; }
    public long ProcessedMessages { get; set; }
    public long FailedMessages { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public long ConsumersCreated { get; set; }
    public long CreationFailures { get; set; }
    public TimeSpan TotalCreationTime { get; set; }
    public TimeSpan AverageCreationTime { get; set; }
    public DateTime LastActivity { get; set; }
    public DateTime LastFailure { get; set; }
    public string? LastFailureReason { get; set; }
}

/// <summary>
/// 統合パフォーマンス統計
/// </summary>
public class KafkaPerformanceStats
{
    public ProducerPerformanceStats ProducerStats { get; set; } = new();
    public ConsumerPerformanceStats ConsumerStats { get; set; } = new();
    public ExtendedCacheStatistics AvroStats { get; set; } = new(); // 既存Avro統計活用
}

// =============================================================================
// Pool Metrics Classes - プール統計
// =============================================================================

/// <summary>
/// プールメトリクス（Producer/Consumer共通）
/// </summary>
public class PoolMetrics
{
    public ProducerKey? ProducerKey { get; set; }
    public ConsumerKey? ConsumerKey { get; set; }
    public long CreatedCount { get; set; }
    public long CreationFailures { get; set; }
    public long RentCount { get; set; }
    public long ReturnCount { get; set; }
    public long DiscardedCount { get; set; }
    public long DisposedCount { get; set; }
    public int ActiveProducers { get; set; }
    public int ActiveConsumers { get; set; }
    public DateTime LastDisposalTime { get; set; }
    public string? LastDisposalReason { get; set; }
    public double FailureRate => CreatedCount > 0 ? (double)CreationFailures / CreatedCount : 0;
}

// =============================================================================
// Health Status Classes - ヘルス状態
// =============================================================================

/// <summary>
/// Kafka全体ヘルス報告
/// </summary>
public class KafkaHealthReport
{
    public DateTime GeneratedAt { get; set; }
    public KafkaHealthLevel HealthLevel { get; set; }

    // コンポーネント別ヘルス
    public ProducerHealthStatus ProducerHealth { get; set; } = new();
    public ConsumerHealthStatus ConsumerHealth { get; set; } = new();
    public CacheHealthReport AvroHealth { get; set; } = new(); // 既存Avro活用

    // 統計情報
    public KafkaPerformanceStats PerformanceStats { get; set; } = new();
    public List<KafkaHealthIssue> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Producer健全性ステータス
/// </summary>
public class ProducerHealthStatus
{
    public ProducerHealthLevel HealthLevel { get; set; }
    public int ActiveProducers { get; set; }
    public PoolHealthStatus PoolHealth { get; set; } = new();
    public ProducerPerformanceStats PerformanceStats { get; set; } = new();
    public List<ProducerHealthIssue> Issues { get; set; } = new();
    public DateTime LastCheck { get; set; }
}

/// <summary>
/// Consumer健全性ステータス
/// </summary>
public class ConsumerHealthStatus
{
    public ConsumerHealthLevel HealthLevel { get; set; }
    public int ActiveConsumers { get; set; }
    public int ActiveSubscriptions { get; set; }
    public ConsumerPoolHealthStatus PoolHealth { get; set; } = new();
    public ConsumerPerformanceStats PerformanceStats { get; set; } = new();
    public List<ConsumerHealthIssue> Issues { get; set; } = new();
    public DateTime LastCheck { get; set; }
}

/// <summary>
/// プール健全性ステータス
/// </summary>
public class PoolHealthStatus
{
    public PoolHealthLevel HealthLevel { get; set; }
    public int TotalPools { get; set; }
    public int TotalActiveProducers { get; set; }
    public int TotalPooledProducers { get; set; }
    public Dictionary<ProducerKey, PoolMetrics> PoolMetrics { get; set; } = new();
    public List<PoolHealthIssue> Issues { get; set; } = new();
    public DateTime LastCheck { get; set; }
}

/// <summary>
/// Consumerプール健全性ステータス
/// </summary>
public class ConsumerPoolHealthStatus
{
    public ConsumerPoolHealthLevel HealthLevel { get; set; }
    public int TotalPools { get; set; }
    public int TotalActiveConsumers { get; set; }
    public int TotalPooledConsumers { get; set; }
    public Dictionary<ConsumerKey, PoolMetrics> PoolMetrics { get; set; } = new();
    public List<ConsumerPoolHealthIssue> Issues { get; set; } = new();
    public DateTime LastCheck { get; set; }
}

// =============================================================================
// Health Issue Classes - 健全性問題
// =============================================================================

/// <summary>
/// Kafka健全性問題
/// </summary>
public class KafkaHealthIssue
{
    public KafkaHealthIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public KafkaIssueSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public string? Recommendation { get; set; }
}

/// <summary>
/// Producer健全性問題
/// </summary>
public class ProducerHealthIssue
{
    public ProducerHealthIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public ProducerIssueSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Consumer健全性問題
/// </summary>
public class ConsumerHealthIssue
{
    public ConsumerHealthIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public ConsumerIssueSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// プール健全性問題
/// </summary>
public class PoolHealthIssue
{
    public PoolHealthIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public PoolIssueSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Consumerプール健全性問題
/// </summary>
public class ConsumerPoolHealthIssue
{
    public ConsumerPoolHealthIssueType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public ConsumerPoolIssueSeverity Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
}

// =============================================================================
// Enum Definitions - 列挙型定義
// =============================================================================

/// <summary>
/// Kafka健全性レベル
/// </summary>
public enum KafkaHealthLevel
{
    Healthy,
    Warning,
    Critical
}

/// <summary>
/// Producer健全性レベル
/// </summary>
public enum ProducerHealthLevel
{
    Healthy,
    Warning,
    Critical
}

/// <summary>
/// Consumer健全性レベル
/// </summary>
public enum ConsumerHealthLevel
{
    Healthy,
    Warning,
    Critical
}

/// <summary>
/// プール健全性レベル
/// </summary>
public enum PoolHealthLevel
{
    Healthy,
    Warning,
    Critical
}

/// <summary>
/// Consumerプール健全性レベル
/// </summary>
public enum ConsumerPoolHealthLevel
{
    Healthy,
    Warning,
    Critical
}

/// <summary>
/// Kafka健全性問題タイプ
/// </summary>
public enum KafkaHealthIssueType
{
    HealthCheckFailure,
    ProducerIssue,
    ConsumerIssue,
    AvroIssue,
    PerformanceIssue,
    ConfigurationIssue
}

/// <summary>
/// Producer健全性問題タイプ
/// </summary>
public enum ProducerHealthIssueType
{
    HighFailureRate,
    HighLatency,
    LowThroughput,
    PoolExhaustion,
    ConfigurationError,
    HealthCheckFailure
}

/// <summary>
/// Consumer健全性問題タイプ
/// </summary>
public enum ConsumerHealthIssueType
{
    HighFailureRate,
    SlowProcessing,
    HighConsumerLag,
    RebalanceIssue,
    PoolExhaustion,
    HealthCheckFailure
}

/// <summary>
/// プール健全性問題タイプ
/// </summary>
public enum PoolHealthIssueType
{
    HighFailureRate,
    PoolExhaustion,
    ResourceLeak,
    HealthCheckFailure
}

/// <summary>
/// Consumerプール健全性問題タイプ
/// </summary>
public enum ConsumerPoolHealthIssueType
{
    HighFailureRate,
    PoolExhaustion,
    RebalanceFailure,
    HealthCheckFailure
}

/// <summary>
/// 問題深刻度
/// </summary>
public enum KafkaIssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// Producer問題深刻度
/// </summary>
public enum ProducerIssueSeverity
{
    Low,
    Medium,
    High,
    Critical,
    Warning
}

/// <summary>
/// Consumer問題深刻度
/// </summary>
public enum ConsumerIssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// プール問題深刻度
/// </summary>
public enum PoolIssueSeverity
{
    Low,
    Medium,
    High,
    Critical,
    Warning
}

/// <summary>
/// Consumerプール問題深刻度
/// </summary>
public enum ConsumerPoolIssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

// =============================================================================
// Diagnostics Classes - 診断情報
// =============================================================================

/// <summary>
/// Kafka診断情報
/// </summary>
public class KafkaDiagnostics
{
    public DateTime GeneratedAt { get; set; }
    public KafkaMessageBusOptions Configuration { get; set; } = new();
    public ProducerDiagnostics ProducerDiagnostics { get; set; } = new();
    public ConsumerDiagnostics ConsumerDiagnostics { get; set; } = new();
    public ExtendedCacheStatistics AvroCache { get; set; } = new(); // 既存Avro統計活用
    public Dictionary<string, object> SystemInfo { get; set; } = new();
}

/// <summary>
/// Producer診断情報
/// </summary>
public class ProducerDiagnostics
{
    public KafkaProducerConfig Configuration { get; set; } = new();
    public ProducerPerformanceStats PerformanceStats { get; set; } = new();
    public PoolDiagnostics PoolDiagnostics { get; set; } = new();
    public Dictionary<Type, ProducerEntityStats> EntityStatistics { get; set; } = new();
    public Dictionary<string, object> SystemMetrics { get; set; } = new();
}

/// <summary>
/// Consumer診断情報
/// </summary>
public class ConsumerDiagnostics
{
    public KafkaConsumerConfig Configuration { get; set; } = new();
    public ConsumerPerformanceStats PerformanceStats { get; set; } = new();
    public ConsumerPoolDiagnostics PoolDiagnostics { get; set; } = new();
    public List<SubscriptionInfo> ActiveSubscriptions { get; set; } = new();
    public Dictionary<Type, ConsumerEntityStats> EntityStatistics { get; set; } = new();
    public Dictionary<string, object> SystemMetrics { get; set; } = new();
}

/// <summary>
/// プール診断情報
/// </summary>
public class PoolDiagnostics
{
    public ProducerPoolConfig Configuration { get; set; } = new();
    public int TotalPools { get; set; }
    public int TotalActiveProducers { get; set; }
    public int TotalPooledProducers { get; set; }
    public Dictionary<ProducerKey, PoolMetrics> PoolMetrics { get; set; } = new();
    public Dictionary<string, object> SystemMetrics { get; set; } = new();
}

/// <summary>
/// Consumerプール診断情報
/// </summary>
public class ConsumerPoolDiagnostics
{
    public ConsumerPoolConfig Configuration { get; set; } = new();
    public int TotalPools { get; set; }
    public int TotalActiveConsumers { get; set; }
    public int TotalPooledConsumers { get; set; }
    public Dictionary<ConsumerKey, PoolMetrics> PoolMetrics { get; set; } = new();
    public Dictionary<string, object> SystemMetrics { get; set; } = new();
}

// =============================================================================
// Extension Methods - 拡張メソッド
// =============================================================================

/// <summary>
/// Kafka設定拡張メソッド
/// </summary>
public static class KafkaConfigurationExtensions
{
    /// <summary>
    /// Producer設定をConfluentProducerConfigに変換
    /// </summary>
    public static ProducerConfig ToConfluentConfig(this KafkaProducerConfig config)
    {
        var confluentConfig = new ProducerConfig
        {
            BootstrapServers = config.BootstrapServers,
            Acks = config.Acks,
            EnableIdempotence = config.EnableIdempotence,
            MaxInFlight = config.MaxInFlight,
            CompressionType = config.CompressionType,
            LingerMs = config.LingerMs,
            BatchSize = config.BatchSize,
            RequestTimeoutMs = config.RequestTimeoutMs,
            DeliveryTimeoutMs = config.DeliveryTimeoutMs,
            RetryBackoffMs = config.RetryBackoffMs,
            SecurityProtocol = config.SecurityProtocol
        };

        if (!string.IsNullOrEmpty(config.SaslMechanism))
        {
            confluentConfig.SaslMechanism = Enum.Parse<SaslMechanism>(config.SaslMechanism);
        }

        if (!string.IsNullOrEmpty(config.SaslUsername))
        {
            confluentConfig.SaslUsername = config.SaslUsername;
        }

        if (!string.IsNullOrEmpty(config.SaslPassword))
        {
            confluentConfig.SaslPassword = config.SaslPassword;
        }

        // 追加設定を適用
        foreach (var kvp in config.AdditionalConfig)
        {
            confluentConfig.Set(kvp.Key, kvp.Value?.ToString());
        }

        return confluentConfig;
    }

    /// <summary>
    /// Consumer設定をConfluentConsumerConfigに変換
    /// </summary>
    public static ConsumerConfig ToConfluentConfig(this KafkaConsumerConfig config, string? groupId = null)
    {
        var confluentConfig = new ConsumerConfig
        {
            BootstrapServers = config.BootstrapServers,
            GroupId = groupId ?? config.DefaultGroupId,
            AutoOffsetReset = config.AutoOffsetReset,
            EnableAutoCommit = config.EnableAutoCommit,
            AutoCommitIntervalMs = config.AutoCommitIntervalMs,
            SessionTimeoutMs = config.SessionTimeoutMs,
            HeartbeatIntervalMs = config.HeartbeatIntervalMs,
            MaxPollIntervalMs = config.MaxPollIntervalMs,
            FetchMinBytes = config.FetchMinBytes,
            FetchMaxWaitMs = config.FetchMaxWaitMs,
            FetchMaxBytes = config.FetchMaxBytes,
            SecurityProtocol = config.SecurityProtocol
        };

        if (!string.IsNullOrEmpty(config.SaslMechanism))
        {
            confluentConfig.SaslMechanism = Enum.Parse<SaslMechanism>(config.SaslMechanism);
        }

        if (!string.IsNullOrEmpty(config.SaslUsername))
        {
            confluentConfig.SaslUsername = config.SaslUsername;
        }

        if (!string.IsNullOrEmpty(config.SaslPassword))
        {
            confluentConfig.SaslPassword = config.SaslPassword;
        }

        // 追加設定を適用
        foreach (var kvp in config.AdditionalConfig)
        {
            confluentConfig.Set(kvp.Key, kvp.Value?.ToString());
        }

        return confluentConfig;
    }

    /// <summary>
    /// ヘルス状態のサマリ文字列を生成
    /// </summary>
    public static string GetHealthSummary(this KafkaHealthReport report)
    {
        var summary = new List<string>
        {
            $"Overall: {report.HealthLevel}",
            $"Producer: {report.ProducerHealth.HealthLevel}",
            $"Consumer: {report.ConsumerHealth.HealthLevel}",
            $"Avro: {report.AvroHealth.HealthLevel}"
        };

        if (report.Issues.Any())
        {
            summary.Add($"Issues: {report.Issues.Count}");
        }

        return string.Join(" | ", summary);
    }

    /// <summary>
    /// パフォーマンス統計のサマリ文字列を生成
    /// </summary>
    public static string GetPerformanceSummary(this KafkaPerformanceStats stats)
    {
        var summary = new List<string>
        {
            $"Producer: {stats.ProducerStats.TotalMessages} msgs, {stats.ProducerStats.FailureRate:P1} fail",
            $"Consumer: {stats.ConsumerStats.TotalMessages} msgs, {stats.ConsumerStats.FailureRate:P1} fail",
            $"Avro: {stats.AvroStats.BaseStatistics.HitRate:P1} hit rate"
        };

        return string.Join(" | ", summary);
    }
}