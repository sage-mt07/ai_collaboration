using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KsqlDsl.Communication;

/// <summary>
/// Producer インスタンスプール
/// 設計理由：Producer作成コスト削減、リソース効率化
/// 動的スケーリング・ヘルス管理による高可用性実現
/// </summary>
public class ProducerPool : IDisposable
{
    private readonly ConcurrentDictionary<ProducerKey, ConcurrentQueue<PooledProducer>> _pools = new();
    private readonly ConcurrentDictionary<ProducerKey, PoolMetrics> _poolMetrics = new();
    private readonly ProducerPoolConfig _config;
    private readonly ILogger<ProducerPool> _logger;
    private readonly Timer _maintenanceTimer;
    private readonly Timer _healthCheckTimer;
    private bool _disposed = false;

    public int MinPoolSize => _config.MinPoolSize;
    public int MaxPoolSize => _config.MaxPoolSize;

    public ProducerPool(
        IOptions<ProducerPoolConfig> config,
        ILogger<ProducerPool> logger)
    {
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 定期メンテナンス（プール最適化・余剰Producer削除）
        _maintenanceTimer = new Timer(PerformMaintenance, null,
            _config.MaintenanceInterval, _config.MaintenanceInterval);

        // ヘルスチェック
        _healthCheckTimer = new Timer(PerformHealthCheck, null,
            _config.HealthCheckInterval, _config.HealthCheckInterval);

        _logger.LogInformation("ProducerPool initialized: Min={MinSize}, Max={MaxSize}, IdleTimeout={IdleTimeout}",
            _config.MinPoolSize, _config.MaxPoolSize, _config.ProducerIdleTimeout);
    }

    /// <summary>
    /// Producer取得
    /// 設計理由：プールからの効率的取得、動的作成による可用性確保
    /// </summary>
    public IProducer<object, object> RentProducer(ProducerKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        var pool = _pools.GetOrAdd(key, _ => new ConcurrentQueue<PooledProducer>());
        var metrics = _poolMetrics.GetOrAdd(key, _ => new PoolMetrics { ProducerKey = key });

        // プールから利用可能Producer検索
        while (pool.TryDequeue(out var pooledProducer))
        {
            if (IsProducerHealthy(pooledProducer))
            {
                pooledProducer.LastUsed = DateTime.UtcNow;
                pooledProducer.UsageCount++;

                lock (metrics)
                {
                    metrics.RentCount++;
                    metrics.ActiveProducers++;
                }

                _logger.LogTrace("Producer rented from pool: {ProducerKey} (Usage: {UsageCount})",
                    key, pooledProducer.UsageCount);

                return pooledProducer.Producer;
            }
            else
            {
                // 不健全なProducerは破棄
                DisposeProducerSafely(pooledProducer.Producer);
                RecordProducerDisposal(key, "unhealthy");
            }
        }

        // プールに利用可能Producerがない場合は新規作成
        var newProducer = CreateNewProducer(key);

        lock (metrics)
        {
            metrics.CreatedCount++;
            metrics.ActiveProducers++;
        }

        _logger.LogDebug("New producer created for key: {ProducerKey} (Total created: {CreatedCount})",
            key, metrics.CreatedCount);

        return newProducer;
    }

    /// <summary>
    /// Producer返却
    /// 設計理由：プールへの効率的な返却、プールサイズ制御
    /// </summary>
    public void ReturnProducer(ProducerKey key, IProducer<object, object> producer)
    {
        if (key == null || producer == null) return;

        try
        {
            var pool = _pools.GetOrAdd(key, _ => new ConcurrentQueue<PooledProducer>());
            var metrics = _poolMetrics.GetOrAdd(key, _ => new PoolMetrics { ProducerKey = key });

            var currentPoolSize = pool.Count;

            // プールサイズ制限チェック
            if (currentPoolSize >= _config.MaxPoolSize)
            {
                // プールが満杯の場合は破棄
                DisposeProducerSafely(producer);
                RecordProducerDisposal(key, "pool_full");

                lock (metrics)
                {
                    metrics.ActiveProducers--;
                    metrics.DiscardedCount++;
                }

                _logger.LogTrace("Producer discarded due to pool limit: {ProducerKey} (Pool size: {PoolSize})",
                    key, currentPoolSize);
                return;
            }

            // プールに返却
            var pooledProducer = new PooledProducer
            {
                Producer = producer,
                CreatedAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow,
                UsageCount = 0
            };

            pool.Enqueue(pooledProducer);

            lock (metrics)
            {
                metrics.ReturnCount++;
                metrics.ActiveProducers--;
            }

            _logger.LogTrace("Producer returned to pool: {ProducerKey} (Pool size: {PoolSize})",
                key, currentPoolSize + 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to return producer to pool: {ProducerKey}", key);
            DisposeProducerSafely(producer);
        }
    }

    /// <summary>
    /// ヘルス状態取得
    /// 設計理由：プール全体の健全性監視、障害検出
    /// </summary>
    public async Task<PoolHealthStatus> GetHealthStatusAsync()
    {
        await Task.Delay(1); // 非同期メソッドの形式保持

        try
        {
            var status = new PoolHealthStatus
            {
                HealthLevel = PoolHealthLevel.Healthy,
                TotalPools = _pools.Count,
                TotalActiveProducers = GetActiveProducerCount(),
                TotalPooledProducers = GetTotalPooledProducers(),
                PoolMetrics = GetAllPoolMetrics(),
                Issues = new List<PoolHealthIssue>(),
                LastCheck = DateTime.UtcNow
            };

            // 健全性問題検出
            var unhealthyPools = 0;
            var overloadedPools = 0;

            foreach (var kvp in _poolMetrics)
            {
                var metrics = kvp.Value;
                var poolSize = _pools.TryGetValue(kvp.Key, out var pool) ? pool.Count : 0;

                lock (metrics)
                {
                    // 失敗率チェック
                    if (metrics.FailureRate > 0.1) // 10%以上の失敗率
                    {
                        unhealthyPools++;
                    }

                    // 過負荷チェック
                    if (poolSize == 0 && metrics.ActiveProducers > _config.MaxPoolSize * 0.8)
                    {
                        overloadedPools++;
                    }
                }
            }

            // ヘルスレベル決定
            if (unhealthyPools > _pools.Count * 0.2) // 20%以上のプールに問題
            {
                status.HealthLevel = PoolHealthLevel.Critical;
                status.Issues.Add(new PoolHealthIssue
                {
                    Type = PoolHealthIssueType.HighFailureRate,
                    Description = $"{unhealthyPools} pools have high failure rates",
                    Severity = PoolIssueSeverity.Critical
                });
            }
            else if (overloadedPools > 0)
            {
                status.HealthLevel = PoolHealthLevel.Warning;
                status.Issues.Add(new PoolHealthIssue
                {
                    Type = PoolHealthIssueType.PoolExhaustion,
                    Description = $"{overloadedPools} pools are experiencing high load",
                    Severity = PoolIssueSeverity.Warning
                });
            }

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pool health status");
            return new PoolHealthStatus
            {
                HealthLevel = PoolHealthLevel.Critical,
                Issues = new List<PoolHealthIssue>
                {
                    new() {
                        Type = PoolHealthIssueType.HealthCheckFailure,
                        Description = $"Health check failed: {ex.Message}",
                        Severity = PoolIssueSeverity.Critical
                    }
                },
                LastCheck = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 余剰Producer削除
    /// 設計理由：メモリ効率化、アイドルリソースの解放
    /// </summary>
    public void TrimExcessProducers()
    {
        var trimCount = 0;
        var now = DateTime.UtcNow;

        foreach (var kvp in _pools)
        {
            var key = kvp.Key;
            var pool = kvp.Value;
            var tempQueue = new ConcurrentQueue<PooledProducer>();

            // アイドル時間チェックで生存Producer選別
            while (pool.TryDequeue(out var pooledProducer))
            {
                var idleTime = now - pooledProducer.LastUsed;

                if (idleTime <= _config.ProducerIdleTimeout &&
                    IsProducerHealthy(pooledProducer) &&
                    tempQueue.Count < _config.MaxPoolSize)
                {
                    tempQueue.Enqueue(pooledProducer);
                }
                else
                {
                    DisposeProducerSafely(pooledProducer.Producer);
                    trimCount++;
                    RecordProducerDisposal(key, idleTime > _config.ProducerIdleTimeout ? "idle_timeout" : "unhealthy");
                }
            }

            // 生存Producerを戻す
            while (tempQueue.TryDequeue(out var survivingProducer))
            {
                pool.Enqueue(survivingProducer);
            }
        }

        if (trimCount > 0)
        {
            _logger.LogInformation("Trimmed {TrimCount} excess producers from pools", trimCount);
        }
    }

    /// <summary>
    /// 診断情報取得
    /// </summary>
    public PoolDiagnostics GetDiagnostics()
    {
        return new PoolDiagnostics
        {
            Configuration = _config,
            TotalPools = _pools.Count,
            TotalActiveProducers = GetActiveProducerCount(),
            TotalPooledProducers = GetTotalPooledProducers(),
            PoolMetrics = GetAllPoolMetrics(),
            SystemMetrics = new Dictionary<string, object>
            {
                ["MemoryUsage"] = GC.GetTotalMemory(false),
                ["ThreadCount"] = System.Diagnostics.Process.GetCurrentProcess().Threads.Count
            }
        };
    }

    public int GetActiveProducerCount()
    {
        return _poolMetrics.Values.Sum(m => m.ActiveProducers);
    }

    private int GetTotalPooledProducers()
    {
        return _pools.Values.Sum(pool => pool.Count);
    }

    private Dictionary<ProducerKey, PoolMetrics> GetAllPoolMetrics()
    {
        return new Dictionary<ProducerKey, PoolMetrics>(_poolMetrics);
    }

    // プライベートヘルパーメソッド

    private IProducer<object, object> CreateNewProducer(ProducerKey key)
    {
        try
        {
            var config = BuildProducerConfig(key);
            var producer = new ProducerBuilder<object, object>(config).Build();

            _logger.LogTrace("New producer created for {ProducerKey}", key);
            return producer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create producer for {ProducerKey}", key);

            var metrics = _poolMetrics.GetOrAdd(key, _ => new PoolMetrics { ProducerKey = key });
            lock (metrics)
            {
                metrics.CreationFailures++;
            }

            throw new ProducerPoolException($"Failed to create producer for key: {key}", ex);
        }
    }

    private ProducerConfig BuildProducerConfig(ProducerKey key)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _config.BootstrapServers,
            Acks = _config.Acks,
            EnableIdempotence = _config.EnableIdempotence,
            MaxInFlight = _config.MaxInFlight,
            CompressionType = _config.CompressionType,
            LingerMs = _config.LingerMs,
            BatchSize = _config.BatchSize
        };

        // カスタム設定適用
        foreach (var kvp in _config.AdditionalConfig)
        {
            config.Set(kvp.Key, kvp.Value);
        }

        return config;
    }

    private bool IsProducerHealthy(PooledProducer pooledProducer)
    {
        if (pooledProducer?.Producer == null) return false;

        try
        {
            // 簡易健全性チェック（実際の実装では更に詳細なチェックが必要）
            var handle = pooledProducer.Producer.Handle;
            return handle != null && !handle.IsClosed;
        }
        catch
        {
            return false;
        }
    }

    private void DisposeProducerSafely(IProducer<object, object> producer)
    {
        try
        {
            producer?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing producer");
        }
    }

    private void RecordProducerDisposal(ProducerKey key, string reason)
    {
        var metrics = _poolMetrics.GetOrAdd(key, _ => new PoolMetrics { ProducerKey = key });

        lock (metrics)
        {
            metrics.DisposedCount++;
            metrics.LastDisposalReason = reason;
            metrics.LastDisposalTime = DateTime.UtcNow;
        }

        _logger.LogTrace("Producer disposed: {ProducerKey}, Reason: {Reason}", key, reason);
    }

    // 定期メンテナンス処理

    private void PerformMaintenance(object? state)
    {
        try
        {
            TrimExcessProducers();

            // プールサイズ最適化
            OptimizePoolSizes();

            _logger.LogTrace("Pool maintenance completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pool maintenance");
        }
    }

    private void PerformHealthCheck(object? state)
    {
        try
        {
            var _ = GetHealthStatusAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during pool health check");
        }
    }

    private void OptimizePoolSizes()
    {
        foreach (var kvp in _pools)
        {
            var key = kvp.Key;
            var pool = kvp.Value;
            var metrics = _poolMetrics.GetOrAdd(key, _ => new PoolMetrics { ProducerKey = key });

            lock (metrics)
            {
                // 使用率に基づく最適サイズ計算
                var utilizationRate = metrics.ActiveProducers > 0 ?
                    (double)metrics.RentCount / (metrics.RentCount + pool.Count) : 0;

                // 低使用率プールの縮小
                if (utilizationRate < 0.1 && pool.Count > _config.MinPoolSize)
                {
                    var targetSize = Math.Max(_config.MinPoolSize, pool.Count / 2);
                    var removeCount = pool.Count - targetSize;

                    for (int i = 0; i < removeCount && pool.TryDequeue(out var producer); i++)
                    {
                        DisposeProducerSafely(producer.Producer);
                        RecordProducerDisposal(key, "pool_optimization");
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing ProducerPool...");

            _maintenanceTimer?.Dispose();
            _healthCheckTimer?.Dispose();

            // 全Producerを破棄
            var totalDisposed = 0;
            foreach (var pool in _pools.Values)
            {
                while (pool.TryDequeue(out var pooledProducer))
                {
                    DisposeProducerSafely(pooledProducer.Producer);
                    totalDisposed++;
                }
            }

            _pools.Clear();
            _poolMetrics.Clear();

            _disposed = true;
            _logger.LogInformation("ProducerPool disposed: {TotalDisposed} producers disposed", totalDisposed);
        }
    }
}