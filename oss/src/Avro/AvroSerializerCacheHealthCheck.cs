using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.Avro
{
    /// <summary>
    /// Avroシリアライザーキャッシュのヘルスチェック実装
    /// 設計理由：.NET標準のHealthCheckシステムと統合し、運用監視を提供
    /// キャッシュのパフォーマンス、ヘルス状態、潜在的な問題を検出
    /// </summary>
    public class AvroSerializerCacheHealthCheck : IHealthCheck
    {
        private readonly PerformanceMonitoringAvroCache _cache;
        private readonly AvroHealthCheckOptions _options;
        private readonly ILogger<AvroSerializerCacheHealthCheck> _logger;

        public AvroSerializerCacheHealthCheck(
            PerformanceMonitoringAvroCache cache,
            IOptions<AvroHealthCheckOptions> options,
            ILogger<AvroSerializerCacheHealthCheck> logger)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// ヘルスチェック実行
        /// 設計理由：複数の観点からキャッシュの健全性を評価
        /// </summary>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                var stats = _cache.GetExtendedStatistics();
                var healthData = new Dictionary<string, object>();
                var issues = new List<string>();
                var warnings = new List<string>();

                // 基本統計の収集
                healthData["cache_hit_rate"] = stats.BaseStatistics.HitRate;
                healthData["cached_items"] = stats.BaseStatistics.CachedItemCount;
                healthData["total_requests"] = stats.BaseStatistics.TotalRequests;
                healthData["total_operations"] = stats.TotalOperations;
                healthData["slow_operations"] = stats.SlowOperationsCount;
                healthData["slow_operation_rate"] = stats.SlowOperationRate;
                healthData["uptime_minutes"] = stats.BaseStatistics.Uptime.TotalMinutes;

                // 1. キャッシュヒット率の評価
                await EvaluateCacheHitRate(stats.BaseStatistics, issues, warnings, healthData);

                // 2. スロー操作率の評価
                await EvaluateSlowOperationRate(stats, issues, warnings, healthData);

                // 3. メモリ使用量の評価
                await EvaluateMemoryUsage(stats.BaseStatistics, issues, warnings, healthData);

                // 4. エンティティ別パフォーマンスの評価
                await EvaluateEntityPerformance(stats.PerformanceMetrics, issues, warnings, healthData);

                // 5. 最近のエラー状況の評価
                await EvaluateRecentErrors(stats, issues, warnings, healthData);

                // 6. キャッシュサイズの評価
                await EvaluateCacheSize(stats.BaseStatistics, issues, warnings, healthData);

                // ヘルス状態の決定
                var healthStatus = DetermineHealthStatus(issues, warnings);

                // 詳細情報の追加
                if (issues.Any() || warnings.Any())
                {
                    healthData["issues"] = issues;
                    healthData["warnings"] = warnings;
                    healthData["recommendations"] = GenerateRecommendations(issues, warnings, stats);
                }

                // ログ出力
                if (healthStatus == HealthStatus.Unhealthy)
                {
                    _logger.LogError("Avro cache health check failed with {IssueCount} critical issues: {Issues}",
                        issues.Count, string.Join("; ", issues));
                }
                else if (warnings.Any())
                {
                    _logger.LogWarning("Avro cache health check passed with {WarningCount} warnings: {Warnings}",
                        warnings.Count, string.Join("; ", warnings));
                }

                return new HealthCheckResult(
                    healthStatus,
                    description: $"Avro Cache Health: {healthStatus}",
                    data: healthData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Avro cache health check encountered an unexpected error");

                return new HealthCheckResult(
                    HealthStatus.Unhealthy,
                    description: "Avro cache health check failed due to internal error",
                    exception: ex);
            }
        }

        /// <summary>
        /// キャッシュヒット率の評価
        /// 設計理由：キャッシュ効果の測定と最適化の指針
        /// </summary>
        private async Task EvaluateCacheHitRate(CacheStatistics baseStats, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            if (baseStats.TotalRequests < _options.MinimumRequestsForEvaluation)
            {
                warnings.Add($"Insufficient request count for evaluation: {baseStats.TotalRequests} < {_options.MinimumRequestsForEvaluation}");
                return;
            }

            if (baseStats.HitRate < _options.CriticalHitRateThreshold)
            {
                issues.Add($"Critical: Cache hit rate too low: {baseStats.HitRate:P2} < {_options.CriticalHitRateThreshold:P2}");
            }
            else if (baseStats.HitRate < _options.WarningHitRateThreshold)
            {
                warnings.Add($"Warning: Cache hit rate below optimal: {baseStats.HitRate:P2} < {_options.WarningHitRateThreshold:P2}");
            }

            healthData["hit_rate_status"] = baseStats.HitRate >= _options.WarningHitRateThreshold ? "good" : "poor";
        }

        /// <summary>
        /// スロー操作率の評価
        /// 設計理由：パフォーマンス劣化の早期検出
        /// </summary>
        private async Task EvaluateSlowOperationRate(ExtendedCacheStatistics stats, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            if (stats.TotalOperations < _options.MinimumOperationsForEvaluation)
            {
                warnings.Add($"Insufficient operation count for slow operation evaluation: {stats.TotalOperations}");
                return;
            }

            if (stats.SlowOperationRate > _options.CriticalSlowOperationRateThreshold)
            {
                issues.Add($"Critical: Slow operation rate too high: {stats.SlowOperationRate:P2} > {_options.CriticalSlowOperationRateThreshold:P2}");
            }
            else if (stats.SlowOperationRate > _options.WarningSlowOperationRateThreshold)
            {
                warnings.Add($"Warning: Slow operation rate elevated: {stats.SlowOperationRate:P2} > {_options.WarningSlowOperationRateThreshold:P2}");
            }

            // 最近のスロー操作の分析
            var recentSlowOps = stats.SlowOperations.Where(op => op.Timestamp > DateTime.UtcNow.AddMinutes(-10)).ToList();
            if (recentSlowOps.Count > _options.MaxRecentSlowOperations)
            {
                warnings.Add($"High number of recent slow operations: {recentSlowOps.Count} in last 10 minutes");
            }

            healthData["slow_operation_status"] = stats.SlowOperationRate <= _options.WarningSlowOperationRateThreshold ? "good" : "poor";
            healthData["recent_slow_operations"] = recentSlowOps.Count;
        }

        /// <summary>
        /// メモリ使用量の評価
        /// 設計理由：メモリリークやキャッシュ膨張の検出
        /// </summary>
        private async Task EvaluateMemoryUsage(CacheStatistics baseStats, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            if (baseStats.CachedItemCount > _options.CriticalCacheSizeThreshold)
            {
                issues.Add($"Critical: Cache size too large: {baseStats.CachedItemCount} > {_options.CriticalCacheSizeThreshold}");
            }
            else if (baseStats.CachedItemCount > _options.WarningCacheSizeThreshold)
            {
                warnings.Add($"Warning: Cache size growing: {baseStats.CachedItemCount} > {_options.WarningCacheSizeThreshold}");
            }

            // メモリ圧迫の兆候チェック
            // 設計理由：GCプレッシャーの間接的な検出
            var currentMemory = GC.GetTotalMemory(false);
            healthData["current_memory_bytes"] = currentMemory;

            // 注意：正確なメモリ圧迫検出には追加のメトリクスが必要
            // TODO: GCメトリクスの統合を検討

            healthData["cache_size_status"] = baseStats.CachedItemCount <= _options.WarningCacheSizeThreshold ? "good" : "warning";
        }

        /// <summary>
        /// エンティティ別パフォーマンスの評価
        /// 設計理由：特定エンティティのパフォーマンス問題を特定
        /// </summary>
        private async Task EvaluateEntityPerformance(Dictionary<string, PerformanceMetrics> performanceMetrics, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            var slowEntities = new List<string>();
            var failingEntities = new List<string>();

            foreach (var kvp in performanceMetrics)
            {
                var entityKey = kvp.Key;
                var metrics = kvp.Value;

                // 平均パフォーマンスのチェック
                if (metrics.AverageDuration.TotalMilliseconds > _options.CriticalAverageOperationTimeMs)
                {
                    slowEntities.Add($"{entityKey} (avg: {metrics.AverageDuration.TotalMilliseconds:F1}ms)");
                }

                // 失敗率のチェック
                if (metrics.SuccessRate < _options.MinimumSuccessRate)
                {
                    failingEntities.Add($"{entityKey} (success: {metrics.SuccessRate:P2})");
                }
            }

            if (slowEntities.Any())
            {
                if (slowEntities.Count > _options.MaxSlowEntitiesBeforeCritical)
                {
                    issues.Add($"Critical: Too many slow entities ({slowEntities.Count}): {string.Join(", ", slowEntities.Take(5))}");
                }
                else
                {
                    warnings.Add($"Slow entities detected: {string.Join(", ", slowEntities)}");
                }
            }

            if (failingEntities.Any())
            {
                issues.Add($"Entities with low success rate: {string.Join(", ", failingEntities)}");
            }

            healthData["slow_entities_count"] = slowEntities.Count;
            healthData["failing_entities_count"] = failingEntities.Count;
        }

        /// <summary>
        /// 最近のエラー状況の評価
        /// 設計理由：エラー傾向の分析と異常検出
        /// </summary>
        private async Task EvaluateRecentErrors(ExtendedCacheStatistics stats, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            // 最近のパフォーマンスメトリクスから失敗数を集計
            var recentFailures = stats.PerformanceMetrics.Values
                .Where(m => m.LastOperation > DateTime.UtcNow.AddMinutes(-30))
                .Sum(m => m.FailureCount);

            var recentOperations = stats.PerformanceMetrics.Values
                .Where(m => m.LastOperation > DateTime.UtcNow.AddMinutes(-30))
                .Sum(m => m.OperationCount);

            if (recentOperations > 0)
            {
                var recentFailureRate = (double)recentFailures / recentOperations;

                if (recentFailureRate > _options.CriticalRecentFailureRateThreshold)
                {
                    issues.Add($"Critical: High recent failure rate: {recentFailureRate:P2} in last 30 minutes");
                }
                else if (recentFailureRate > _options.WarningRecentFailureRateThreshold)
                {
                    warnings.Add($"Warning: Elevated recent failure rate: {recentFailureRate:P2} in last 30 minutes");
                }

                healthData["recent_failure_rate"] = recentFailureRate;
            }

            healthData["recent_failures"] = recentFailures;
            healthData["recent_operations"] = recentOperations;
        }

        /// <summary>
        /// キャッシュサイズの評価
        /// 設計理由：キャッシュの成長傾向とメモリ効率性の監視
        /// </summary>
        private async Task EvaluateCacheSize(CacheStatistics baseStats, List<string> issues, List<string> warnings, Dictionary<string, object> healthData)
        {
            await Task.Delay(1); // 非同期メソッドの形式保持

            // キャッシュ効率性の評価
            if (baseStats.TotalRequests > 0)
            {
                var cacheEfficiency = (double)baseStats.CachedItemCount / baseStats.TotalRequests;

                if (cacheEfficiency > _options.MaxCacheEfficiencyRatio)
                {
                    warnings.Add($"Cache efficiency may be low: {baseStats.CachedItemCount} cached items for {baseStats.TotalRequests} requests");
                }

                healthData["cache_efficiency"] = cacheEfficiency;
            }

            // キャッシュ成長率の評価
            if (baseStats.Uptime.TotalHours > 1) // 1時間以上稼働している場合
            {
                var itemsPerHour = baseStats.CachedItemCount / baseStats.Uptime.TotalHours;

                if (itemsPerHour > _options.MaxCacheGrowthRatePerHour)
                {
                    warnings.Add($"High cache growth rate: {itemsPerHour:F1} items/hour");
                }

                healthData["cache_growth_rate_per_hour"] = itemsPerHour;
            }
        }

        /// <summary>
        /// ヘルス状態の決定
        /// 設計理由：issue/warningの組み合わせから総合的な健全性を判定
        /// </summary>
        private static HealthStatus DetermineHealthStatus(List<string> issues, List<string> warnings)
        {
            if (issues.Any())
            {
                return HealthStatus.Unhealthy;
            }

            if (warnings.Count >= 3) // 設計理由：多数の警告は重大な問題の前兆
            {
                return HealthStatus.Degraded;
            }

            if (warnings.Any())
            {
                return HealthStatus.Degraded;
            }

            return HealthStatus.Healthy;
        }

        /// <summary>
        /// 推奨事項の生成
        /// 設計理由：運用担当者への具体的な改善提案を提供
        /// </summary>
        private List<string> GenerateRecommendations(List<string> issues, List<string> warnings, ExtendedCacheStatistics stats)
        {
            var recommendations = new List<string>();

            // ヒット率改善の推奨
            if (stats.BaseStatistics.HitRate < _options.WarningHitRateThreshold)
            {
                recommendations.Add("Consider pre-warming cache with frequently used schemas");
                recommendations.Add("Review schema registration patterns and timing");
            }

            // スロー操作改善の推奨
            if (stats.SlowOperationRate > _options.WarningSlowOperationRateThreshold)
            {
                recommendations.Add("Investigate slow entities and optimize schema generation");
                recommendations.Add("Consider increasing cache size or adjusting thresholds");
            }

            // メモリ使用量改善の推奨
            if (stats.BaseStatistics.CachedItemCount > _options.WarningCacheSizeThreshold)
            {
                recommendations.Add("Consider implementing cache expiration policies");
                recommendations.Add("Review cache size limits and memory allocation");
            }

            // パフォーマンス改善の推奨
            var slowEntities = stats.PerformanceMetrics.Values.Count(m => m.AverageDuration.TotalMilliseconds > _options.CriticalAverageOperationTimeMs);
            if (slowEntities > 0)
            {
                recommendations.Add($"Optimize schema generation for {slowEntities} slow entities");
                recommendations.Add("Consider async schema registration for heavy entities");
            }

            return recommendations;
        }
    }

    /// <summary>
    /// Avroヘルスチェック設定オプション
    /// 設計理由：ヘルスチェック基準の外部設定可能化
    /// </summary>
    public class AvroHealthCheckOptions
    {
        /// <summary>
        /// 警告レベルのキャッシュヒット率閾値（デフォルト: 70%）
        /// </summary>
        public double WarningHitRateThreshold { get; set; } = 0.70;

        /// <summary>
        /// 危険レベルのキャッシュヒット率閾値（デフォルト: 50%）
        /// </summary>
        public double CriticalHitRateThreshold { get; set; } = 0.50;

        /// <summary>
        /// 警告レベルのスロー操作率閾値（デフォルト: 5%）
        /// </summary>
        public double WarningSlowOperationRateThreshold { get; set; } = 0.05;

        /// <summary>
        /// 危険レベルのスロー操作率閾値（デフォルト: 15%）
        /// </summary>
        public double CriticalSlowOperationRateThreshold { get; set; } = 0.15;

        /// <summary>
        /// 警告レベルのキャッシュサイズ閾値（デフォルト: 5000）
        /// </summary>
        public int WarningCacheSizeThreshold { get; set; } = 5000;

        /// <summary>
        /// 危険レベルのキャッシュサイズ閾値（デフォルト: 10000）
        /// </summary>
        public int CriticalCacheSizeThreshold { get; set; } = 10000;

        /// <summary>
        /// 危険レベルの平均操作時間閾値（ミリ秒）（デフォルト: 200ms）
        /// </summary>
        public double CriticalAverageOperationTimeMs { get; set; } = 200.0;

        /// <summary>
        /// 最小成功率閾値（デフォルト: 95%）
        /// </summary>
        public double MinimumSuccessRate { get; set; } = 0.95;

        /// <summary>
        /// 評価に必要な最小リクエスト数（デフォルト: 100）
        /// </summary>
        public long MinimumRequestsForEvaluation { get; set; } = 100;

        /// <summary>
        /// 評価に必要な最小操作数（デフォルト: 50）
        /// </summary>
        public long MinimumOperationsForEvaluation { get; set; } = 50;

        /// <summary>
        /// 最近のスロー操作の最大許容数（10分間）（デフォルト: 20）
        /// </summary>
        public int MaxRecentSlowOperations { get; set; } = 20;

        /// <summary>
        /// 危険認定前の最大スローエンティティ数（デフォルト: 5）
        /// </summary>
        public int MaxSlowEntitiesBeforeCritical { get; set; } = 5;

        /// <summary>
        /// 警告レベルの最近の失敗率閾値（30分間）（デフォルト: 2%）
        /// </summary>
        public double WarningRecentFailureRateThreshold { get; set; } = 0.02;

        /// <summary>
        /// 危険レベルの最近の失敗率閾値（30分間）（デフォルト: 10%）
        /// </summary>
        public double CriticalRecentFailureRateThreshold { get; set; } = 0.10;

        /// <summary>
        /// 最大キャッシュ効率比率（デフォルト: 2.0）
        /// 設計理由：リクエスト数に対するキャッシュ項目数の比率上限
        /// </summary>
        public double MaxCacheEfficiencyRatio { get; set; } = 2.0;

        /// <summary>
        /// 最大キャッシュ成長率（1時間あたり）（デフォルト: 1000）
        /// </summary>
        public double MaxCacheGrowthRatePerHour { get; set; } = 1000.0;
    }

    /// <summary>
    /// Avroヘルスチェック拡張メソッド
    /// </summary>
    public static class AvroHealthCheckExtensions
    {
        public static void AddAvroHealthCheck(
            this object services,
            string name = "avro_cache",
            object? failureStatus = null,
            object? tags = null,
            TimeSpan? timeout = null)
        {
            // プレースホルダー実装
            // 実際のDI統合時に適切な実装に置き換える
        }
    }
}
