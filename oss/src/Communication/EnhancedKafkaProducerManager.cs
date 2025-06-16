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

namespace KsqlDsl.Communication
{
    /// <summary>
    /// 強化版Producer管理（既存Avro統合・プール管理・型安全性）
    /// 設計理由：既存ProducerPoolとEnhancedAvroSerializerManagerを統合し、
    /// エンティティ別最適化とパフォーマンス監視を実現
    /// </summary>
    public class EnhancedKafkaProducerManager : IDisposable
    {
        private readonly EnhancedAvroSerializerManager _serializerManager;
        private readonly ProducerPool _producerPool;
        private readonly KafkaProducerConfig _config;
        private readonly ILogger<EnhancedKafkaProducerManager> _logger;

        // 型安全Producerキャッシュ（型別管理）
        private readonly ConcurrentDictionary<Type, object> _typedProducers = new();

        // エンティティ別統計（既存実装を拡張）
        private readonly ConcurrentDictionary<Type, ProducerEntityStats> _entityStats = new();
        private readonly ProducerPerformanceStats _performanceStats = new();
        private bool _disposed = false;

        public EnhancedKafkaProducerManager(
            EnhancedAvroSerializerManager serializerManager,
            ProducerPool producerPool,
            IOptions<KafkaProducerConfig> config,
            ILogger<EnhancedKafkaProducerManager> logger)
        {
            _serializerManager = serializerManager ?? throw new ArgumentNullException(nameof(serializerManager));
            _producerPool = producerPool ?? throw new ArgumentNullException(nameof(producerPool));
            _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("Enhanced ProducerManager initialized: Pool={MinSize}-{MaxSize}",
                _producerPool.MinPoolSize, _producerPool.MaxPoolSize);
        }

        /// <summary>
        /// 型安全Producer取得（キャッシュ・プール統合）
        /// 設計理由：型別にProducerを最適化し、既存プールと統合
        /// </summary>
        public async Task<IKafkaProducer<T>> GetProducerAsync<T>() where T : class
        {
            var entityType = typeof(T);

            // 型安全Producerキャッシュから取得
            if (_typedProducers.TryGetValue(entityType, out var cachedProducer))
            {
                return (IKafkaProducer<T>)cachedProducer;
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // EntityModel取得（既存実装活用）
                var entityModel = GetEntityModel<T>();
                var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;

                // プールからraw Producer取得
                var producerKey = new ProducerKey(entityType, topicName, _config.GetKeyHash());
                var rawProducer = _producerPool.RentProducer(producerKey);

                // 既存EnhancedAvroSerializerManagerでシリアライザー取得
                var (keySerializer, valueSerializer) = await _serializerManager.CreateSerializersAsync<T>(entityModel);

                // 型安全Producerラッパー作成
                var typedProducer = new TypedKafkaProducer<T>(
                    rawProducer,
                    keySerializer,
                    valueSerializer,
                    topicName,
                    entityModel,
                    _logger);

                // キャッシュに登録
                _typedProducers.TryAdd(entityType, typedProducer);

                stopwatch.Stop();
                RecordProducerCreation<T>(stopwatch.Elapsed);

                _logger.LogDebug("Enhanced Producer created: {EntityType} -> {TopicName} ({Duration}ms)",
                    entityType.Name, topicName, stopwatch.ElapsedMilliseconds);

                return typedProducer;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordProducerCreationFailure<T>(stopwatch.Elapsed, ex);

                _logger.LogError(ex, "Failed to create enhanced producer: {EntityType} ({Duration}ms)",
                    entityType.Name, stopwatch.ElapsedMilliseconds);

                throw new KafkaProducerManagerException($"Failed to create producer for {entityType.Name}", ex);
            }
        }

        /// <summary>
        /// バッチ送信最適化（型安全・高性能）
        /// 設計理由：同一エンティティのバッチ処理に特化し、既存メトリクスと統合
        /// </summary>
        public async Task<KafkaBatchDeliveryResult> SendBatchOptimizedAsync<T>(
            IEnumerable<T> messages,
            KafkaMessageContext? context = null,
            CancellationToken cancellationToken = default) where T : class
        {
            if (messages == null)
                throw new ArgumentNullException(nameof(messages));

            var messageList = messages.ToList();
            if (messageList.Count == 0)
                return new KafkaBatchDeliveryResult { AllSuccessful = true };

            using var activity = KafkaActivitySource.StartActivity("kafka.batch_send_optimized")
                ?.SetTag("kafka.entity.type", typeof(T).Name)
                ?.SetTag("kafka.batch.size", messageList.Count);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var producer = await GetProducerAsync<T>();
                var result = await producer.SendBatchAsync(messageList, context, cancellationToken);

                stopwatch.Stop();

                // 既存統計システムとの統合
                RecordBatchSend<T>(messageList.Count, result.AllSuccessful, stopwatch.Elapsed);

                activity?.SetTag("kafka.batch.successful", result.SuccessfulCount)
                        ?.SetTag("kafka.batch.failed", result.FailedCount)
                        ?.SetStatus(result.AllSuccessful ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordBatchSend<T>(messageList.Count, success: false, stopwatch.Elapsed);

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                _logger.LogError(ex, "Optimized batch send failed: {EntityType}, {MessageCount} messages",
                    typeof(T).Name, messageList.Count);
                throw;
            }
        }

        /// <summary>
        /// Producer返却・プール統合
        /// 設計理由：プールとの一貫した管理、リソース効率化
        /// </summary>
        public void ReturnProducer<T>(IKafkaProducer<T> producer) where T : class
        {
            if (producer == null) return;

            try
            {
                if (producer is TypedKafkaProducer<T> typedProducer)
                {
                    var entityType = typeof(T);
                    var producerKey = new ProducerKey(entityType, typedProducer.TopicName, _config.GetKeyHash());

                    // 注意：TypedKafkaProducerは内部でraw producerを保持
                    // プール返却のためにはraw producerの参照が必要（設計改善要）
                    _logger.LogTrace("Producer logically returned: {EntityType} -> {TopicName}",
                        entityType.Name, typedProducer.TopicName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to return producer: {EntityType}", typeof(T).Name);
            }
        }

        /// <summary>
        /// パフォーマンス統計取得（既存統合版）
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
        /// 健全性ステータス取得（既存プール統合）
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

                // 健全性問題検出（既存閾値活用）
                if (stats.FailureRate > 0.1)
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
                        Description = $"High latency: {stats.AverageLatency.TotalMilliseconds:F0}ms",
                        Severity = ProducerIssueSeverity.Warning
                    });
                }

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get enhanced producer health status");

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
        /// アクティブProducer数取得
        /// </summary>
        public int GetActiveProducerCount() => _producerPool.GetActiveProducerCount();

        // プライベートヘルパーメソッド

        private EntityModel GetEntityModel<T>() where T : class
        {
            // 実際の実装では、ModelBuilderまたはキャッシュから取得
            // 暫定実装として簡略化
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

            // グローバル統計更新
            lock (_performanceStats)
            {
                _performanceStats.TotalMessages += messageCount;
                _performanceStats.TotalBatches++;

                if (successful)
                    _performanceStats.SuccessfulMessages += messageCount;
                else
                    _performanceStats.FailedMessages += messageCount;

                // スループット計算
                var now = DateTime.UtcNow;
                if (_performanceStats.LastThroughputCalculation == default)
                {
                    _performanceStats.LastThroughputCalculation = now;
                }
                else
                {
                    var elapsed = now - _performanceStats.LastThroughputCalculation;
                    if (elapsed.TotalSeconds >= 60)
                    {
                        _performanceStats.ThroughputPerSecond = _performanceStats.TotalMessages / elapsed.TotalSeconds;
                        _performanceStats.LastThroughputCalculation = now;
                    }
                }

                if (_performanceStats.TotalBatches > 0)
                {
                    _performanceStats.AverageLatency = TimeSpan.FromTicks(
                        stats.TotalSendTime.Ticks / _performanceStats.TotalBatches);
                }
            }
        }

        private ProducerHealthLevel DetermineHealthLevel(PoolHealthStatus poolHealth, ProducerPerformanceStats stats)
        {
            if (poolHealth.HealthLevel == PoolHealthLevel.Critical)
                return ProducerHealthLevel.Critical;

            if (stats.FailureRate > 0.2)
                return ProducerHealthLevel.Critical;

            if (stats.AverageLatency.TotalMilliseconds > _config.HealthThresholds.CriticalLatencyMs)
                return ProducerHealthLevel.Critical;

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
                _logger.LogInformation("Disposing Enhanced ProducerManager...");

                // キャッシュクリア
                _typedProducers.Clear();

                // 統計の最終出力
                var finalStats = GetPerformanceStats();
                _logger.LogInformation("Final Enhanced Producer Statistics: Messages={TotalMessages}, Batches={TotalBatches}, SuccessRate={SuccessRate:P2}",
                    finalStats.TotalMessages, finalStats.TotalBatches, 1.0 - finalStats.FailureRate);

                _producerPool?.Dispose();
                _entityStats.Clear();

                _disposed = true;
                _logger.LogInformation("Enhanced ProducerManager disposed successfully");
            }
        }
    }
}