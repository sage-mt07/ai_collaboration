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
/// Consumer管理・購読制御
/// 設計理由：複数購読の効率管理、オフセット管理の統合
/// 既存EnhancedAvroSerializerManagerとの統合により型安全なデシリアライゼーション実現
/// </summary>
public class KafkaConsumerManager : IDisposable
{
    private readonly EnhancedAvroSerializerManager _serializerManager;
    private readonly ConsumerPool _consumerPool;
    private readonly KafkaConsumerConfig _config;
    private readonly ILogger<KafkaConsumerManager> _logger;

    // Consumer統計・パフォーマンス追跡
    private readonly ConcurrentDictionary<Type, ConsumerEntityStats> _entityStats = new();
    private readonly ConsumerPerformanceStats _performanceStats = new();
    private readonly ConcurrentDictionary<string, ConsumerSubscription> _activeSubscriptions = new();
    private bool _disposed = false;

    public KafkaConsumerManager(
        EnhancedAvroSerializerManager serializerManager,
        ConsumerPool consumerPool,
        IOptions<KafkaConsumerConfig> config,
        ILogger<KafkaConsumerManager> logger)
    {
        _serializerManager = serializerManager ?? throw new ArgumentNullException(nameof(serializerManager));
        _consumerPool = consumerPool ?? throw new ArgumentNullException(nameof(consumerPool));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("KafkaConsumerManager initialized with pool config: Min={MinSize}, Max={MaxSize}",
            _consumerPool.MinPoolSize, _consumerPool.MaxPoolSize);
    }

    /// <summary>
    /// 型安全Consumer作成
    /// 設計理由：型ごとの最適化されたConsumerインスタンス提供、購読状態管理
    /// </summary>
    public async Task<IKafkaConsumer<T>> CreateConsumerAsync<T>(KafkaSubscriptionOptions options) where T : class
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var entityType = typeof(T);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // EntityModelから設定情報取得（既存実装活用）
            var entityModel = GetEntityModel<T>();
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;

            // Consumer設定構築
            var consumerKey = new ConsumerKey(entityType, topicName, options.GroupId ?? _config.DefaultGroupId);

            // プールからConsumer取得
            var rawConsumer = _consumerPool.RentConsumer(consumerKey);

            // Avroデシリアライザー取得（既存実装活用）
            var (keyDeserializer, valueDeserializer) = await _serializerManager.CreateDeserializersAsync<T>(entityModel);

            // 型安全Consumerラッパー作成
            var typedConsumer = new KafkaConsumer<T>(
                rawConsumer,
                keyDeserializer,
                valueDeserializer,
                topicName,
                entityModel,
                options,
                this,
                _logger);

            stopwatch.Stop();

            // 統計更新
            RecordConsumerCreation<T>(stopwatch.Elapsed);

            _logger.LogDebug("Consumer created for {EntityType} -> {TopicName} ({Duration}ms)",
                entityType.Name, topicName, stopwatch.ElapsedMilliseconds);

            return typedConsumer;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Failed to create consumer for {EntityType} ({Duration}ms)",
                entityType.Name, stopwatch.ElapsedMilliseconds);

            // 失敗統計更新
            RecordConsumerCreationFailure<T>(stopwatch.Elapsed, ex);

            throw new KafkaConsumerManagerException($"Failed to create consumer for {entityType.Name}", ex);
        }
    }

    /// <summary>
    /// 購読開始
    /// 設計理由：型安全な購読管理、ハンドラーベースの処理
    /// </summary>
    public async Task SubscribeAsync<T>(
        Func<T, KafkaMessageContext, Task> handler,
        KafkaSubscriptionOptions options,
        CancellationToken cancellationToken = default) where T : class
    {
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var entityType = typeof(T);
        var subscriptionId = GenerateSubscriptionId<T>(options);

        if (_activeSubscriptions.ContainsKey(subscriptionId))
        {
            throw new InvalidOperationException($"Subscription already exists for {entityType.Name} with key: {subscriptionId}");
        }

        var consumer = await CreateConsumerAsync<T>(options);

        var subscription = new ConsumerSubscription
        {
            Id = subscriptionId,
            EntityType = entityType,
            Consumer = consumer,
            Handler = async (obj, ctx) => await handler((T)obj, ctx),
            Options = options,
            StartedAt = DateTime.UtcNow,
            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        };

        _activeSubscriptions[subscriptionId] = subscription;

        // バックグラウンドでメッセージ処理開始
        _ = Task.Run(async () => await ProcessSubscriptionAsync(subscription), cancellationToken);

        _logger.LogInformation("Subscription started: {EntityType} -> {SubscriptionId}", entityType.Name, subscriptionId);
    }

    /// <summary>
    /// 購読停止
    /// </summary>
    public async Task UnsubscribeAsync<T>(string? groupId = null) where T : class
    {
        var entityType = typeof(T);
        var options = new KafkaSubscriptionOptions { GroupId = groupId };
        var subscriptionId = GenerateSubscriptionId<T>(options);

        if (_activeSubscriptions.TryRemove(subscriptionId, out var subscription))
        {
            subscription.CancellationTokenSource.Cancel();
            subscription.Consumer.Dispose();

            _logger.LogInformation("Subscription stopped: {EntityType} -> {SubscriptionId}", entityType.Name, subscriptionId);
        }

        await Task.Delay(1); // 非同期メソッドの形式保持
    }

    /// <summary>
    /// バッチ消費
    /// 設計理由：高スループット処理、効率的なバッチ処理API
    /// </summary>
    public async IAsyncEnumerable<KafkaBatch<T>> ConsumeBatchesAsync<T>(
        KafkaBatchOptions options,
        CancellationToken cancellationToken = default) where T : class
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        using var activity = KafkaActivitySource.StartActivity("kafka.consume_batches")
            ?.SetTag("kafka.entity.type", typeof(T).Name)
            ?.SetTag("kafka.batch.max_size", options.MaxBatchSize)
            ?.SetTag("kafka.batch.max_wait_ms", options.MaxWaitTime.TotalMilliseconds);

        var consumer = await CreateConsumerAsync<T>(new KafkaSubscriptionOptions
        {
            GroupId = options.ConsumerGroupId,
            AutoCommit = options.AutoCommit,
            EnablePartitionEof = true
        });

        try
        {
            var batchCount = 0;
            var totalMessages = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await consumer.ConsumeBatchAsync(options, cancellationToken);

                if (batch.Messages.Count > 0)
                {
                    batchCount++;
                    totalMessages += batch.Messages.Count;

                    // バッチメトリクス記録
                    RecordBatchConsume<T>(batch.Messages.Count, batch.ProcessingTime);

                    yield return batch;
                }
                else if (options.EnableEmptyBatches)
                {
                    // 空バッチも返却（タイムアウト検出用）
                    yield return batch;
                }

                // 定期的なメトリクス更新
                if (batchCount % 10 == 0)
                {
                    activity?.SetTag("kafka.batches.processed", batchCount)
                            ?.SetTag("kafka.messages.processed", totalMessages);
                }
            }

            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to consume batches for {EntityType}", typeof(T).Name);
            throw new KafkaConsumerManagerException($"Failed to consume {typeof(T).Name} batches", ex);
        }
        finally
        {
            consumer.Dispose();
        }
    }

    /// <summary>
    /// オフセットコミット
    /// 設計理由：明示的なオフセット管理、トランザクション境界制御
    /// </summary>
    public async Task CommitAsync<T>(string? groupId = null) where T : class
    {
        var entityType = typeof(T);
        var options = new KafkaSubscriptionOptions { GroupId = groupId };
        var subscriptionId = GenerateSubscriptionId<T>(options);

        if (_activeSubscriptions.TryGetValue(subscriptionId, out var subscription))
        {
            await subscription.Consumer.CommitAsync();
            _logger.LogDebug("Offset committed for {EntityType}", entityType.Name);
        }
        else
        {
            _logger.LogWarning("No active subscription found for commit: {EntityType}", entityType.Name);
        }
    }

    /// <summary>
    /// オフセットシーク
    /// 設計理由：履歴データ処理、障害復旧時のポジション制御
    /// </summary>
    public async Task SeekAsync<T>(TopicPartitionOffset offset, string? groupId = null) where T : class
    {
        if (offset == null)
            throw new ArgumentNullException(nameof(offset));

        var entityType = typeof(T);
        var options = new KafkaSubscriptionOptions { GroupId = groupId };
        var subscriptionId = GenerateSubscriptionId<T>(options);

        if (_activeSubscriptions.TryGetValue(subscriptionId, out var subscription))
        {
            await subscription.Consumer.SeekAsync(offset);
            _logger.LogInformation("Seeked to offset for {EntityType}: {TopicPartitionOffset}",
                entityType.Name, offset);
        }
        else
        {
            _logger.LogWarning("No active subscription found for seek: {EntityType}", entityType.Name);
        }
    }

    /// <summary>
    /// パフォーマンス統計取得
    /// 設計理由：運用監視、チューニング指標の提供
    /// </summary>
    public ConsumerPerformanceStats GetPerformanceStats()
    {
        var stats = new ConsumerPerformanceStats
        {
            TotalMessages = _performanceStats.TotalMessages,
            TotalBatches = _performanceStats.TotalBatches,
            ProcessedMessages = _performanceStats.ProcessedMessages,
            FailedMessages = _performanceStats.FailedMessages,
            AverageProcessingTime = _performanceStats.AverageProcessingTime,
            ThroughputPerSecond = _performanceStats.ThroughputPerSecond,
            ActiveConsumers = GetActiveConsumerCount(),
            ActiveSubscriptions = _activeSubscriptions.Count,
            EntityStats = GetEntityStats(),
            LastUpdated = DateTime.UtcNow
        };

        return stats;
    }

    /// <summary>
    /// 健全性ステータス取得
    /// 設計理由：障害検出、自動復旧判断のための情報提供
    /// </summary>
    public async Task<ConsumerHealthStatus> GetHealthStatusAsync()
    {
        try
        {
            var poolHealth = await _consumerPool.GetHealthStatusAsync();
            var stats = GetPerformanceStats();

            var status = new ConsumerHealthStatus
            {
                HealthLevel = DetermineHealthLevel(poolHealth, stats),
                ActiveConsumers = stats.ActiveConsumers,
                ActiveSubscriptions = stats.ActiveSubscriptions,
                PoolHealth = poolHealth,
                PerformanceStats = stats,
                Issues = new List<ConsumerHealthIssue>(),
                LastCheck = DateTime.UtcNow
            };

            // 健全性問題検出
            if (stats.FailureRate > 0.1) // 10%以上の失敗率
            {
                status.Issues.Add(new ConsumerHealthIssue
                {
                    Type = ConsumerHealthIssueType.HighFailureRate,
                    Description = $"High failure rate: {stats.FailureRate:P2}",
                    Severity = ConsumerIssueSeverity.Warning
                });
            }

            if (stats.AverageProcessingTime.TotalMilliseconds > _config.HealthThresholds.MaxAverageProcessingTimeMs)
            {
                status.Issues.Add(new ConsumerHealthIssue
                {
                    Type = ConsumerHealthIssueType.SlowProcessing,
                    Description = $"Slow processing: {stats.AverageProcessingTime.TotalMilliseconds:F0}ms",
                    Severity = ConsumerIssueSeverity.Warning
                });
            }

            // コンシューマーラグチェック
            var lagIssues = await CheckConsumerLagAsync();
            status.Issues.AddRange(lagIssues);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get consumer health status");
            return new ConsumerHealthStatus
            {
                HealthLevel = ConsumerHealthLevel.Critical,
                Issues = new List<ConsumerHealthIssue>
                {
                    new() {
                        Type = ConsumerHealthIssueType.HealthCheckFailure,
                        Description = $"Health check failed: {ex.Message}",
                        Severity = ConsumerIssueSeverity.Critical
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
    public ConsumerDiagnostics GetDiagnostics()
    {
        return new ConsumerDiagnostics
        {
            Configuration = _config,
            PerformanceStats = GetPerformanceStats(),
            PoolDiagnostics = _consumerPool.GetDiagnostics(),
            ActiveSubscriptions = GetActiveSubscriptionInfo(),
            EntityStatistics = GetEntityStats(),
            SystemMetrics = new Dictionary<string, object>
            {
                ["ActiveConsumers"] = GetActiveConsumerCount(),
                ["ActiveSubscriptions"] = _activeSubscriptions.Count,
                ["TotalEntityTypes"] = _entityStats.Count,
                ["ConfigurationHash"] = _config.GetKeyHash()
            }
        };
    }

    /// <summary>
    /// アクティブConsumer数取得
    /// </summary>
    public int GetActiveConsumerCount() => _consumerPool.GetActiveConsumerCount();

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

    private string GenerateSubscriptionId<T>(KafkaSubscriptionOptions options) where T : class
    {
        var entityType = typeof(T);
        var groupId = options.GroupId ?? _config.DefaultGroupId;
        return $"{entityType.Name}:{groupId}:{options.GetHashCode()}";
    }

    private async Task ProcessSubscriptionAsync(ConsumerSubscription subscription)
    {
        var cancellationToken = subscription.CancellationTokenSource.Token;

        try
        {
            await foreach (var kafkaMessage in subscription.Consumer.ConsumeAsync(cancellationToken))
            {
                var context = new KafkaMessageContext
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Headers = ExtractHeaders(kafkaMessage),
                    // 他のコンテキスト情報を設定
                };

                try
                {
                    await subscription.Handler(kafkaMessage.Value, context);

                    // 成功統計更新
                    RecordMessageProcessed(subscription.EntityType, success: true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Message processing failed for {EntityType}", subscription.EntityType.Name);

                    // 失敗統計更新
                    RecordMessageProcessed(subscription.EntityType, success: false);

                    // エラーハンドリング戦略（設定に応じて）
                    if (subscription.Options.StopOnError)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Subscription cancelled: {EntityType}", subscription.EntityType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Subscription processing error: {EntityType}", subscription.EntityType.Name);
        }
        finally
        {
            _activeSubscriptions.TryRemove(subscription.Id, out _);
        }
    }

    private Dictionary<string, object> ExtractHeaders<T>(KafkaMessage<T> kafkaMessage) where T : class
    {
        var headers = new Dictionary<string, object>();

        if (kafkaMessage.Headers != null)
        {
            foreach (var header in kafkaMessage.Headers)
            {
                if (header.GetValueBytes() != null)
                {
                    headers[header.Key] = System.Text.Encoding.UTF8.GetString(header.GetValueBytes());
                }
            }
        }

        return headers;
    }

    private void RecordConsumerCreation<T>(TimeSpan duration)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ConsumerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.ConsumersCreated++;
            stats.TotalCreationTime += duration;
            stats.AverageCreationTime = TimeSpan.FromTicks(stats.TotalCreationTime.Ticks / stats.ConsumersCreated);
            stats.LastActivity = DateTime.UtcNow;
        }

        Interlocked.Increment(ref _performanceStats.TotalConsumersCreated);
    }

    private void RecordConsumerCreationFailure<T>(TimeSpan duration, Exception ex)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ConsumerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.CreationFailures++;
            stats.LastFailure = DateTime.UtcNow;
            stats.LastFailureReason = ex.Message;
        }

        Interlocked.Increment(ref _performanceStats.ConsumerCreationFailures);
    }

    private void RecordBatchConsume<T>(int messageCount, TimeSpan processingTime)
    {
        var entityType = typeof(T);
        var stats = _entityStats.GetOrAdd(entityType, _ => new ConsumerEntityStats { EntityType = entityType });

        lock (stats)
        {
            stats.TotalMessages += messageCount;
            stats.TotalBatches++;
            stats.TotalProcessingTime += processingTime;
            stats.AverageProcessingTime = TimeSpan.FromTicks(stats.TotalProcessingTime.Ticks / stats.TotalBatches);
            stats.LastActivity = DateTime.UtcNow;
        }

        // 全体統計更新
        lock (_performanceStats)
        {
            _performanceStats.TotalMessages += messageCount;
            _performanceStats.TotalBatches++;
            _performanceStats.ProcessedMessages += messageCount;

            // スループット計算
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

            // 平均処理時間計算
            if (_performanceStats.TotalBatches > 0)
            {
                _performanceStats.AverageProcessingTime = TimeSpan.FromTicks(
                    stats.TotalProcessingTime.Ticks / _performanceStats.TotalBatches);
            }
        }
    }

    private void RecordMessageProcessed(Type entityType, bool success)
    {
        var stats = _entityStats.GetOrAdd(entityType, _ => new ConsumerEntityStats { EntityType = entityType });

        lock (stats)
        {
            if (success)
            {
                stats.ProcessedMessages++;
            }
            else
            {
                stats.FailedMessages++;
            }
            stats.LastActivity = DateTime.UtcNow;
        }

        if (success)
        {
            Interlocked.Increment(ref _performanceStats.ProcessedMessages);
        }
        else
        {
            Interlocked.Increment(ref _performanceStats.FailedMessages);
        }
    }

    private ConsumerHealthLevel DetermineHealthLevel(ConsumerPoolHealthStatus poolHealth, ConsumerPerformanceStats stats)
    {
        // プール健全性を最優先で判定
        if (poolHealth.HealthLevel == ConsumerPoolHealthLevel.Critical)
            return ConsumerHealthLevel.Critical;

        // パフォーマンス指標チェック
        if (stats.FailureRate > 0.2) // 20%以上の失敗率
            return ConsumerHealthLevel.Critical;

        if (stats.AverageProcessingTime.TotalMilliseconds > _config.HealthThresholds.CriticalProcessingTimeMs)
            return ConsumerHealthLevel.Critical;

        // 警告レベルチェック
        if (poolHealth.HealthLevel == ConsumerPoolHealthLevel.Warning ||
            stats.FailureRate > 0.1 ||
            stats.AverageProcessingTime.TotalMilliseconds > _config.HealthThresholds.MaxAverageProcessingTimeMs)
            return ConsumerHealthLevel.Warning;

        return ConsumerHealthLevel.Healthy;
    }

    private async Task<List<ConsumerHealthIssue>> CheckConsumerLagAsync()
    {
        var issues = new List<ConsumerHealthIssue>();

        // 実際の実装では、Admin APIを使用してコンシューマーラグを取得
        // ここでは簡略実装
        await Task.Delay(1);

        return issues;
    }

    private Dictionary<Type, ConsumerEntityStats> GetEntityStats()
    {
        return new Dictionary<Type, ConsumerEntityStats>(_entityStats);
    }

    private List<SubscriptionInfo> GetActiveSubscriptionInfo()
    {
        return _activeSubscriptions.Values.Select(s => new SubscriptionInfo
        {
            Id = s.Id,
            EntityType = s.EntityType,
            StartedAt = s.StartedAt,
            Options = s.Options
        }).ToList();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing KafkaConsumerManager...");

            // アクティブ購読の停止
            foreach (var subscription in _activeSubscriptions.Values)
            {
                subscription.CancellationTokenSource.Cancel();
                subscription.Consumer.Dispose();
            }
            _activeSubscriptions.Clear();

            // 統計情報の最終出力
            var finalStats = GetPerformanceStats();
            _logger.LogInformation("Final Consumer Statistics: Messages={TotalMessages}, Batches={TotalBatches}, SuccessRate={SuccessRate:P2}",
                finalStats.TotalMessages, finalStats.TotalBatches, 1.0 - finalStats.FailureRate);

            _consumerPool?.Dispose();
            _entityStats.Clear();

            _disposed = true;
            _logger.LogInformation("KafkaConsumerManager disposed successfully");
        }
    }
}