using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using KsqlDsl.Avro;
using KsqlDsl.Modeling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KsqlDsl.Communication;

/// <summary>
/// Producer管理・ライフサイクル制御
/// 設計理由：複数トピック・エンティティへの効率的Producer配布
/// 既存EnhancedAvroSerializerManagerとの統合により型安全なシリアライゼーション実現
/// </summary>
public class KafkaProducerManager : IDisposable
{
    private readonly EnhancedAvroSerializerManager _serializerManager;
    private readonly ProducerPool _producerPool;
    private readonly KafkaProducerConfig _config;
    private readonly ILogger<KafkaProducerManager> _logger;

    // Producer統計・パフォーマンス追跡
    private readonly ConcurrentDictionary<Type, ProducerEntityStats> _entityStats = new();
    private readonly ProducerPerformanceStats _performanceStats = new();
    private bool _disposed = false;

    public KafkaProducerManager(
        EnhancedAvroSerializerManager serializerManager,
        ProducerPool producerPool,
        IOptions<KafkaProducerConfig> config,
        ILogger<KafkaProducerManager> logger)
    {
        _serializerManager = serializerManager ?? throw new ArgumentNullException(nameof(serializerManager));
        _producerPool = producerPool ?? throw new ArgumentNullException(nameof(producerPool));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("KafkaProducerManager initialized with pool config: Min={MinSize}, Max={MaxSize}",
            _producerPool.MinPoolSize, _producerPool.MaxPoolSize);
    }

    /// <summary>
    /// 型安全Producer取得
    /// 設計理由：型ごとの最適化されたProducerインスタンス提供、プールからの効率的取得
    /// </summary>
    public async Task<IKafkaProducer<T>> GetProducerAsync<T>() where T : class
    {
        var entityType = typeof(T);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // EntityModelから設定情報取得（既存実装活用）
            var entityModel = GetEntityModel<T>();
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;

            // Producer設定構築
            var producerKey = new ProducerKey(entityType, topicName, _config.GetKeyHash());

            // プールからProducer取得
            var rawProducer = _producerPool.RentProducer(producerKey);

            // Avroシリアライザー取得（既存実装活用）
            var (keySerializer, valueSerializer) = await _serializerManager.CreateSerializersAsync<T>(entityModel);

            // 型安全Producerラッパー作成
            var typedProducer = new KafkaProducer<T>(
                rawProducer,
                keySerializer,
                valueSerializer,
                topicName,
                entityModel,
                this,
                _logger);

            stopwatch.Stop();

            // 統計更新
            RecordProducerCreation<T>(stopwatch.Elapsed);

            _logger.LogDebug("Producer created for {EntityType} -> {TopicName} ({Duration}ms)",
                entityType.Name, topicName, stopwatch.ElapsedMilliseconds);

            return typedProducer;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Failed to create producer for {EntityType} ({Duration}ms)",
                entityType.Name, stopwatch.ElapsedMilliseconds);

            // 失敗統計更新
            RecordProducerCreationFailure<T>(stopwatch.Elapsed, ex);

            throw new KafkaProducerManagerException($"Failed to create producer for {entityType.Name}", ex);
        }
    }

    /// <summary>
    /// Producer返却
    /// 設計理由：プールへの効率的な返却、リソース再利用
    /// </summary>
    public void ReturnProducer<T>(IKafkaProducer<T> producer) where T : class
    {
        if (producer == null) return;

        try
        {
            if (producer is KafkaProducer<T> typedProducer)
            {
                var producerKey = new ProducerKey(typeof(T), typedProducer.TopicName, _config.GetKeyHash());
                _producerPool.ReturnProducer(producerKey, typedProducer.RawProducer);

                _logger.LogTrace("Producer returned to pool: {EntityType} -> {TopicName}",
                    typeof(T).Name, typedProducer.TopicName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to return producer to pool: {EntityType}", typeof(T).Name);
            // プール返却失敗は致命的でないため、エラーは記録のみ
        }
    }

    /// <summary>
    /// バッチ送信最適化
    /// 設計理由：同一トピック・パーティションのメッセージをグルーピングして効率的送信
    /// </summary>
    public async Task<KafkaBatchDeliveryResult> SendBatchOptimizedAsync<T>(
        IEnumerable<T> messages,
        KafkaMessageContext? context,
        CancellationToken cancellationToken = default) where T : class
    {
        if (messages == null)
            throw new ArgumentNullException(nameof(messages));

        var messageList = messages.ToList();
        if (messageList.Count == 0)
            return new KafkaBatchDeliveryResult { AllSuccessful = true };

        var stopwatch = Stopwatch.StartNew();
        var producer = await GetProducerAsync<T>();

        try
        {
            // バッチ送信実行
            var result = await producer.SendBatchAsync(messageList, context, cancellationToken);
            stopwatch.Stop();

            // バッチ統計更新
            RecordBatchSend<T>(messageList.Count, result.AllSuccessful, stopwatch.Elapsed);

            return result;
        }
        finally
        {
            ReturnProducer(producer);
        }
    }

    /// <summary>
    /// パフォーマンス統計取得
    /// 設計理由：運用監視、チューニング指標の提供
    /// </summary>
    public ProducerPerformanceStats GetPerformanceStats()
    {
        var stats = new ProducerPerformanceStats
        {
            TotalMessages = _performanceStats.TotalMessages,
            TotalBatches = _performanceStats.TotalBatches,
            SuccessfulMessages = _performanceStats.SuccessfulMessages,
            FailedMessages = _performanceStats.FailedMessages,
            AverageLatency = _performanceStats.AverageLatency,
            ThroughputPerSecond = _performanceStats.ThroughputPerSecond,
            ActiveProducers = GetActiveProducerCount(),
            EntityStats = GetEntityStats(),
            LastUpdated = DateTime.UtcNow
        };

        return stats;
    }

    /// <summary>
    /// 健全性ステータス取得
    /// 設計理由：障害検出、自動復旧判断のための情報提供
    /// </summary>
    public async Task<ProducerHealthStatus> GetHealthStatusAsync()
    {
        try
        {
            var poolHealth = await _producerPool.GetHealthStatusAsync();
            var stats = GetPerformanceStats();

            var status = new ProducerHealthStatus
            {
                HealthLevel = DetermineHealthLevel(poolHealth, stats),
                ActiveProducers = stats.ActiveProducers,
                PoolHealth = poolHealth,
                PerformanceStats = stats,
                Issues = new List<ProducerHealthIssue>(),
                LastCheck = DateTime.UtcNow
            };

            // 健全性問題検出
            if (stats.FailureRate > 0.1) // 10%以上の失敗率
            {
                status.Issues.Add(new ProducerHealthIssue
                {
                    Type = ProducerHealthIssueType.HighFailureRate,
                    Description = $"High failure rate: {stats.FailureRate:P2}",
                    Severity = ProducerIssueSeverity.Warning
                });
            }

            if (stats.AverageLatency.TotalMilliseconds > _config.HealthThresholds.MaxAverageLatencyMs)
            {
                status.Issues.Add(new ProducerHealthIssue
                {
                    Type = ProducerHealthIssueType.HighLatency,
                    Description = $"High average latency: {stats.AverageLatency.TotalMilliseconds:F0}ms",
                    Severity = ProducerIssueSeverity.Warning
                });
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get producer health status");
            return new ProducerHealthStatus
            {
                HealthLevel = ProducerHealthLevel.Critical,
                Issues = new List<ProducerHealthIssue>
                {
                    new() {
                        Type = ProducerHealthIssueType.HealthCheckFailure,
                        Description = $"Health check failed: {ex.Message}",
                        Severity = ProducerIssueSeverity.Critical
                    }
                },
                LastCheck = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 診断情報取得
    /// 設計理由：トラブルシューティング支援
    /// </summary>
    public ProducerDiagnostics GetDiagnostics()
    {
        return new ProducerDiagnostics
        {
            Configuration = _config,
            PerformanceStats = GetPerformanceStats(),
            PoolDiagnostics = _producerPool.GetDiagnostics(),
            EntityStatistics = GetEntityStats(),
            SystemMetrics = new Dictionary<string, object>
            {
                ["ActiveProducers"] = GetActiveProducerCount(),
                ["TotalEntityTypes"] = _entityStats.Count,
                ["ConfigurationHash"] = _config.GetKeyHash()
            }
        };
    }

    /// <summary>
    /// アクティブProducer数取得
    /// </summary>
    public int GetActiveProducerCount() => _producerPool.GetActiveProducerCount();

    // プライベートヘルパーメソッド

    private EntityModel GetEntityModel<T>() where T : class
    {
        // 実際の実装では、ModelBuilderまたはキャッシュから取得
        // ここでは簡略実装
        return new EntityModel
        {
            EntityType = typeof(T),
            TopicAttribute = new KsqlDsl.Attributes.TopicAttribute(typeof(T).Name)
        };
    }

    private void RecordProducerCreation<T>(TimeSpan duration)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ProducerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.ProducersCreated++;
            stats.TotalCreationTime += duration;
            stats.AverageCreationTime = TimeSpan.FromTicks(stats.TotalCreationTime.Ticks / stats.ProducersCreated);
            stats.LastActivity = DateTime.UtcNow;
        }

        // 全体統計更新
        Interlocked.Increment(ref _performanceStats.TotalProducersCreated);
    }

    private void RecordProducerCreationFailure<T>(TimeSpan duration, Exception ex)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ProducerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.CreationFailures++;
            stats.LastFailure = DateTime.UtcNow;
            stats.LastFailureReason = ex.Message;
        }

        Interlocked.Increment(ref _performanceStats.ProducerCreationFailures);
    }

    private void RecordBatchSend<T>(int messageCount, bool successful, TimeSpan duration)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ProducerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.TotalMessages += messageCount;
            stats.TotalBatches++;

            if (successful)
            {
                stats.SuccessfulMessages += messageCount;
                stats.SuccessfulBatches++;
            }
            else
            {
                stats.FailedMessages += messageCount;
                stats.FailedBatches++;
            }

            stats.TotalSendTime += duration;
            stats.AverageSendTime = TimeSpan.FromTicks(stats.TotalSendTime.Ticks / stats.TotalBatches);
            stats.LastActivity = DateTime.UtcNow;
        }

        // 全体統計更新
        lock (_performanceStats)
        {
            _performanceStats.TotalMessages += messageCount;
            _performanceStats.TotalBatches++;

            if (successful)
            {
                _performanceStats.SuccessfulMessages += messageCount;
            }
            else
            {
                _performanceStats.FailedMessages += messageCount;
            }

            // スループット計算（直近1分間）
            var now = DateTime.UtcNow;
            if (_performanceStats.LastThroughputCalculation == default)
            {
                _performanceStats.LastThroughputCalculation = now;
            }
            else
            {
                var elapsed = now - _performanceStats.LastThroughputCalculation;
                if (elapsed.TotalSeconds >= 60) // 1分毎に更新
                {
                    _performanceStats.ThroughputPerSecond = _performanceStats.TotalMessages / elapsed.TotalSeconds;
                    _performanceStats.LastThroughputCalculation = now;
                }
            }

            // 平均レイテンシ計算
            if (_performanceStats.TotalBatches > 0)
            {
                _performanceStats.AverageLatency = TimeSpan.FromTicks(
                    stats.TotalSendTime.Ticks / _performanceStats.TotalBatches);
            }
        }
    }

    private ProducerHealthLevel DetermineHealthLevel(PoolHealthStatus poolHealth, ProducerPerformanceStats stats)
    {
        // プール健全性を最優先で判定
        if (poolHealth.HealthLevel == PoolHealthLevel.Critical)
            return ProducerHealthLevel.Critical;

        // パフォーマンス指標チェック
        if (stats.FailureRate > 0.2) // 20%以上の失敗率
            return ProducerHealthLevel.Critical;

        if (stats.AverageLatency.TotalMilliseconds > _config.HealthThresholds.CriticalLatencyMs)
            return ProducerHealthLevel.Critical;

        // 警告レベルチェック
        if (poolHealth.HealthLevel == PoolHealthLevel.Warning ||
            stats.FailureRate > 0.1 ||
            stats.AverageLatency.TotalMilliseconds > _config.HealthThresholds.MaxAverageLatencyMs)
            return ProducerHealthLevel.Warning;

        return ProducerHealthLevel.Healthy;
    }

    private Dictionary<Type, ProducerEntityStats> GetEntityStats()
    {
        return new Dictionary<Type, ProducerEntityStats>(_entityStats);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing KafkaProducerManager...");

            // 統計情報の最終出力
            var finalStats = GetPerformanceStats();
            _logger.LogInformation("Final Producer Statistics: Messages={TotalMessages}, Batches={TotalBatches}, SuccessRate={SuccessRate:P2}",
                finalStats.TotalMessages, finalStats.TotalBatches, 1.0 - finalStats.FailureRate);

            _producerPool?.Dispose();
            _entityStats.Clear();

            _disposed = true;
            _logger.LogInformation("KafkaProducerManager disposed successfully");
        }
    }
}