using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace KsqlDsl.Avro
{
    public class PerformanceMonitoringAvroCache : AvroSerializerCache
    {
        private readonly PerformanceThresholds _thresholds;
        private readonly ILogger<PerformanceMonitoringAvroCache> _logger;

        public PerformanceMonitoringAvroCache(
            IOptions<PerformanceThresholds> thresholds,
            ILogger<PerformanceMonitoringAvroCache> logger) : base(logger)
        {
            _thresholds = thresholds?.Value ?? throw new ArgumentNullException(nameof(thresholds));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public new ISerializer<object> GetOrCreateSerializer<T>(SerializerType type, int schemaId, Func<ISerializer<object>> factory)
        {
            using var activity = AvroActivitySource.StartCacheOperation("get_or_create", typeof(T).Name);
            var stopwatch = Stopwatch.StartNew();

            var result = base.GetOrCreateSerializer<T>(type, schemaId, factory);
            stopwatch.Stop();

            AvroMetrics.RecordSerializationDuration(typeof(T).Name, type.ToString(), stopwatch.Elapsed);

            if (stopwatch.ElapsedMilliseconds > _thresholds.SlowSerializerCreationMs)
            {
                AvroLogMessages.SlowSerializerCreation(_logger, typeof(T).Name, type.ToString(), schemaId,
                    stopwatch.ElapsedMilliseconds, _thresholds.SlowSerializerCreationMs);
            }

            var hitRate = GetEntityCacheStatus<T>().OverallHitRate;
            if (hitRate < _thresholds.MinimumHitRate)
            {
                AvroLogMessages.LowCacheHitRate(_logger, typeof(T).Name, hitRate, _thresholds.MinimumHitRate);
                ConsiderCacheOptimization<T>();
            }

            return result;
        }

        public new IDeserializer<object> GetOrCreateDeserializer<T>(SerializerType type, int schemaId, Func<IDeserializer<object>> factory)
        {
            using var activity = AvroActivitySource.StartCacheOperation("get_or_create_deserializer", typeof(T).Name);
            var stopwatch = Stopwatch.StartNew();

            var result = base.GetOrCreateDeserializer<T>(type, schemaId, factory);
            stopwatch.Stop();

            AvroMetrics.RecordSerializationDuration(typeof(T).Name, type.ToString(), stopwatch.Elapsed);

            if (stopwatch.ElapsedMilliseconds > _thresholds.SlowSerializerCreationMs)
            {
                AvroLogMessages.SlowSerializerCreation(_logger, typeof(T).Name, type.ToString(), schemaId,
                    stopwatch.ElapsedMilliseconds, _thresholds.SlowSerializerCreationMs);
            }

            var hitRate = GetEntityCacheStatus<T>().OverallHitRate;
            if (hitRate < _thresholds.MinimumHitRate)
            {
                AvroLogMessages.LowCacheHitRate(_logger, typeof(T).Name, hitRate, _thresholds.MinimumHitRate);
                ConsiderCacheOptimization<T>();
            }

            return result;
        }

        private void ConsiderCacheOptimization<T>()
        {
            var entityStats = GetEntityCacheStatus<T>();

            if (entityStats.AllMisses > 100 && entityStats.OverallHitRate < 0.5)
            {
                _logger.LogInformation("Considering cache preload for {EntityType} due to frequent misses",
                    typeof(T).Name);

                _ = Task.Run(() => PreloadFrequentlyUsedSchemasAsync<T>());
            }
        }

        private async Task PreloadFrequentlyUsedSchemasAsync<T>()
        {
            try
            {
                _logger.LogInformation("Starting cache preload for {EntityType}", typeof(T).Name);

                // よく使われるスキーマをプリロードするロジック
                // 実装は要件に応じて調整
                await Task.Delay(100); // プレースホルダー

                _logger.LogInformation("Cache preload completed for {EntityType}", typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache preload failed for {EntityType}", typeof(T).Name);
            }
        }

        public new CacheHealthReport GetHealthReport()
        {
            var report = base.GetHealthReport();

            // パフォーマンス閾値ベースの追加チェック
            var stats = GetGlobalStatistics();

            if (stats.C