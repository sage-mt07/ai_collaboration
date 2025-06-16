using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.Avro
{
    /// <summary>
    /// 高性能監視機能付きAvroシリアライザーキャッシュ
    /// 設計理由：AvroSerializerCacheを継承し、パフォーマンス監視機能を追加
    /// メトリクス収集、スロークエリ検出、統計情報の詳細化を実装
    /// </summary>
    public class PerformanceMonitoringAvroCache : AvroSerializerCache
    {
        private readonly ILogger<PerformanceMonitoringAvroCache>? _logger;
        private readonly PerformanceThresholds _thresholds;

        // パフォーマンス監視用フィールド
        private readonly ConcurrentDictionary<string, PerformanceMetrics> _entityMetrics = new();
        private readonly ConcurrentQueue<SlowOperationRecord> _slowOperations = new();
        private readonly Timer _metricsReportTimer;
        private long _totalOperations;
        private long _slowOperationsCount;
        private DateTime _lastMetricsReport = DateTime.UtcNow;

        public PerformanceMonitoringAvroCache(
            ILogger<PerformanceMonitoringAvroCache>? logger = null,
            PerformanceThresholds? thresholds = null) : base(logger)
        {
            _logger = logger;
            _thresholds = thresholds ?? new PerformanceThresholds();

            // 定期的なメトリクスレポート（5分間隔）
            _metricsReportTimer = new Timer(ReportMetrics, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// パフォーマンス監視付きシリアライザー取得
        /// 設計理由：基底クラスの機能を拡張し、操作時間を測定・記録
        /// </summary>
        public override ISerializer<object> GetOrCreateSerializer<T>(SerializerType type, int schemaId, Func<ISerializer<object>> factory)
        {
            var stopwatch = Stopwatch.StartNew();
            var entityTypeName = typeof(T).Name;

            using var activity = AvroActivitySource.StartCacheOperation("get_or_create_serializer", entityTypeName);

            try
            {
                var result = base.GetOrCreateSerializer<T>(type, schemaId, factory);
                stopwatch.Stop();

                // パフォーマンスメトリクス記録
                RecordOperation(entityTypeName, "Serializer", type.ToString(), stopwatch.Elapsed, true);

                // スロー操作検出
                if (stopwatch.ElapsedMilliseconds > _thresholds.SlowSerializerCreationMs)
                {
                    RecordSlowOperation(entityTypeName, "GetOrCreateSerializer", type.ToString(), stopwatch.Elapsed);

                    AvroLogMessages.SlowSerializerCreation(
                        _logger, entityTypeName, type.ToString(), schemaId,
                        stopwatch.ElapsedMilliseconds, _thresholds.SlowSerializerCreationMs);
                }

                // メトリクス記録
                AvroMetrics.RecordSerializationDuration(entityTypeName, type.ToString(), stopwatch.Elapsed);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordOperation(entityTypeName, "Serializer", type.ToString(), stopwatch.Elapsed, false);

                _logger?.LogError(ex,
                    "Serializer creation failed: {EntityType}:{SerializerType}:{SchemaId} (Duration: {Duration}ms)",
                    entityTypeName, type.ToString(), schemaId, stopwatch.ElapsedMilliseconds);

                throw;
            }
        }

        /// <summary>
        /// パフォーマンス監視付きデシリアライザー取得
        /// 設計理由：シリアライザーと同様の監視機能をデシリアライザーにも適用
        /// </summary>
        public override IDeserializer<object> GetOrCreateDeserializer<T>(SerializerType type, int schemaId, Func<IDeserializer<object>> factory)
        {
            var stopwatch = Stopwatch.StartNew();
            var entityTypeName = typeof(T).Name;

            using var activity = AvroActivitySource.StartCacheOperation("get_or_create_deserializer", entityTypeName);

            try
            {
                var result = base.GetOrCreateDeserializer<T>(type, schemaId, factory);
                stopwatch.Stop();

                // パフォーマンスメトリクス記録
                RecordOperation(entityTypeName, "Deserializer", type.ToString(), stopwatch.Elapsed, true);

                // スロー操作検出
                if (stopwatch.ElapsedMilliseconds > _thresholds.SlowSerializerCreationMs)
                {
                    RecordSlowOperation(entityTypeName, "GetOrCreateDeserializer", type.ToString(), stopwatch.Elapsed);

                    AvroLogMessages.SlowSerializerCreation(
                        _logger, entityTypeName, $"Deserializer-{type}", schemaId,
                        stopwatch.ElapsedMilliseconds, _thresholds.SlowSerializerCreationMs);
                }

                // メトリクス記録
                AvroMetrics.RecordSerializationDuration(entityTypeName, $"Deserializer-{type}", stopwatch.Elapsed);

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                RecordOperation(entityTypeName, "Deserializer", type.ToString(), stopwatch.Elapsed, false);

                _logger?.LogError(ex,
                    "Deserializer creation failed: {EntityType}:{SerializerType}:{SchemaId} (Duration: {Duration}ms)",
                    entityTypeName, type.ToString(), schemaId, stopwatch.ElapsedMilliseconds);

                throw;
            }
        }

        /// <summary>
        /// 操作パフォーマンスの記録
        /// 設計理由：エンティティ別の詳細なパフォーマンス統計を取得
        /// </summary>
        private void RecordOperation(string entityTypeName, string operationType, string serializerType, TimeSpan duration, bool success)
        {
            Interlocked.Increment(ref _totalOperations);

            var key = $"{entityTypeName}:{operationType}:{serializerType}";
            var metrics = _entityMetrics.GetOrAdd(key, _ => new PerformanceMetrics());

            lock (metrics) // 設計理由：統計計算の原子性を保証
            {
                metrics.OperationCount++;
                metrics.TotalDuration += duration;

                if (success)
                {
                    metrics.SuccessCount++;
                }
                else
                {
                    metrics.FailureCount++;
                }

                // 最小・最大・平均値の更新
                if (duration < metrics.MinDuration || metrics.MinDuration == TimeSpan.Zero)
                    metrics.MinDuration = duration;

                if (duration > metrics.MaxDuration)
                    metrics.MaxDuration = duration;

                metrics.AverageDuration = TimeSpan.FromTicks(metrics.TotalDuration.Ticks / metrics.OperationCount);
                metrics.LastOperation = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// スロー操作の記録
        /// 設計理由：パフォーマンス問題の詳細な分析のため、スロー操作を別途記録
        /// </summary>
        private void RecordSlowOperation(string entityTypeName, string operationType, string serializerType, TimeSpan duration)
        {
            Interlocked.Increment(ref _slowOperationsCount);

            var slowOp = new SlowOperationRecord
            {
                EntityTypeName = entityTypeName,
                OperationType = operationType,
                SerializerType = serializerType,
                Duration = duration,
                Timestamp = DateTime.UtcNow
            };

            _slowOperations.Enqueue(slowOp);

            // スロー操作履歴の制限（最新1000件）
            // 設計理由：メモリ使用量の制御
            while (_slowOperations.Count > 1000)
            {
                _slowOperations.TryDequeue(out _);
            }
        }

        /// <summary>
        /// 拡張統計情報の取得
        /// 設計理由：詳細なパフォーマンス分析のための包括的な統計情報
        /// </summary>
        public ExtendedCacheStatistics GetExtendedStatistics()
        {
            var baseStats = GetGlobalStatistics();
            var entityStats = GetAllEntityStatuses();

            return new ExtendedCacheStatistics
            {
                BaseStatistics = baseStats,
                EntityStatistics = entityStats,
                PerformanceMetrics = GetPerformanceMetrics(),
                SlowOperations = GetRecentSlowOperations(),
                TotalOperations = _totalOperations,
                SlowOperationsCount = _slowOperationsCount,
                SlowOperationRate = _totalOperations > 0 ? (double)_slowOperationsCount / _totalOperations : 0.0,
                LastMetricsReport = _lastMetricsReport
            };
        }

        /// <summary>
        /// エンティティ別パフォーマンスメトリクスの取得
        /// </summary>
        public Dictionary<string, PerformanceMetrics> GetPerformanceMetrics()
        {
            return new Dictionary<string, PerformanceMetrics>(_entityMetrics);
        }

        /// <summary>
        /// 最近のスロー操作履歴の取得
        /// </summary>
        public List<SlowOperationRecord> GetRecentSlowOperations(int maxCount = 100)
        {
            return _slowOperations.TakeLast(maxCount).ToList();
        }

        /// <summary>
        /// パフォーマンス統計のリセット
        /// 設計理由：長期運用時の統計リセット機能
        /// </summary>
        public void ResetPerformanceMetrics()
        {
            _entityMetrics.Clear();
            while (_slowOperations.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _totalOperations, 0);
            Interlocked.Exchange(ref _slowOperationsCount, 0);
            _lastMetricsReport = DateTime.UtcNow;

            _logger?.LogInformation("Performance metrics have been reset");
        }

        /// <summary>
        /// 定期的なメトリクスレポート
        /// 設計理由：運用監視のための定期的な統計レポート
        /// </summary>
        private void ReportMetrics(object? state)
        {
            try
            {
                var stats = GetExtendedStatistics();

                _logger?.LogInformation(
                    "Performance Report - Total Operations: {TotalOps}, Slow Operations: {SlowOps} ({SlowRate:P2}), " +
                    "Cache Hit Rate: {HitRate:P2}, Cached Items: {CacheSize}",
                    stats.TotalOperations, stats.SlowOperationsCount, stats.SlowOperationRate,
                    stats.BaseStatistics.HitRate, stats.BaseStatistics.CachedItemCount);

                // 低パフォーマンスエンティティの警告
                foreach (var kvp in stats.PerformanceMetrics)
                {
                    var metrics = kvp.Value;
                    if (metrics.AverageDuration.TotalMilliseconds > _thresholds.SlowSerializerCreationMs)
                    {
                        _logger?.LogWarning(
                            "Slow entity detected: {EntityKey} - Avg: {AvgMs}ms, Max: {MaxMs}ms, Operations: {Ops}",
                            kvp.Key, metrics.AverageDuration.TotalMilliseconds,
                            metrics.MaxDuration.TotalMilliseconds, metrics.OperationCount);
                    }
                }

                _lastMetricsReport = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during performance metrics reporting");
            }
        }

        /// <summary>
        /// リソース解放
        /// 設計理由：タイマーの適切な解放
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _metricsReportTimer?.Dispose();
                _entityMetrics.Clear();
                while (_slowOperations.TryDequeue(out _)) { }
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// パフォーマンスメトリクス
    /// 設計理由：エンティティ別の詳細なパフォーマンス統計
    /// </summary>
    public class PerformanceMetrics
    {
        public long OperationCount { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan MinDuration { get; set; }
        public TimeSpan MaxDuration { get; set; }
        public TimeSpan AverageDuration { get; set; }
        public DateTime LastOperation { get; set; }
        public double SuccessRate => OperationCount > 0 ? (double)SuccessCount / OperationCount : 0.0;
    }

    /// <summary>
    /// スロー操作記録
    /// 設計理由：パフォーマンス問題の詳細分析
    /// </summary>
    public class SlowOperationRecord
    {
        public string EntityTypeName { get; set; } = string.Empty;
        public string OperationType { get; set; } = string.Empty;
        public string SerializerType { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 拡張キャッシュ統計
    /// 設計理由：基本統計とパフォーマンス統計を統合
    /// </summary>
    public class ExtendedCacheStatistics
    {
        public CacheStatistics BaseStatistics { get; set; } = null!;
        public Dictionary<Type, EntityCacheStatus> EntityStatistics { get; set; } = new();
        public Dictionary<string, PerformanceMetrics> PerformanceMetrics { get; set; } = new();
        public List<SlowOperationRecord> SlowOperations { get; set; } = new();
        public long TotalOperations { get; set; }
        public long SlowOperationsCount { get; set; }
        public double SlowOperationRate { get; set; }
        public DateTime LastMetricsReport { get; set; }
    }
}