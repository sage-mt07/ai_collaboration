using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace KsqlDsl.Avro
{
    public class AvroMetrics
    {
        private static readonly Meter _meter = new("KsqlDsl.Avro", "1.0.0");

        // カウンター
        private static readonly Counter<long> _cacheHitCounter =
            _meter.CreateCounter<long>("avro_cache_hits_total", description: "Total cache hits");
        private static readonly Counter<long> _cacheMissCounter =
            _meter.CreateCounter<long>("avro_cache_misses_total", description: "Total cache misses");
        private static readonly Counter<long> _schemaRegistrationCounter =
            _meter.CreateCounter<long>("avro_schema_registrations_total", description: "Total schema registrations");

        // ヒストグラム
        private static readonly Histogram<double> _serializationDuration =
            _meter.CreateHistogram<double>("avro_serialization_duration_ms", "ms", "Serialization duration");
        private static readonly Histogram<double> _schemaRegistrationDuration =
            _meter.CreateHistogram<double>("avro_schema_registration_duration_ms", "ms", "Schema registration duration");

        public static void RecordCacheHit(string entityType, string serializerType)
        {
            _cacheHitCounter.Add(1,
                new KeyValuePair<string, object?>("entity_type", entityType),
                new KeyValuePair<string, object?>("serializer_type", serializerType));
        }

        public static void RecordCacheMiss(string entityType, string serializerType)
        {
            _cacheMissCounter.Add(1,
                new KeyValuePair<string, object?>("entity_type", entityType),
                new KeyValuePair<string, object?>("serializer_type", serializerType));
        }

        public static void RecordSerializationDuration(string entityType, string serializerType, TimeSpan duration)
        {
            _serializationDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("entity_type", entityType),
                new KeyValuePair<string, object?>("serializer_type", serializerType));
        }

        public static void RecordSchemaRegistration(string subject, bool success, TimeSpan duration)
        {
            _schemaRegistrationCounter.Add(1,
                new KeyValuePair<string, object?>("subject", subject),
                new KeyValuePair<string, object?>("success", success));

            _schemaRegistrationDuration.Record(duration.TotalMilliseconds,
                new KeyValuePair<string, object?>("subject", subject),
                new KeyValuePair<string, object?>("success", success));
        }

        public static void UpdateGlobalMetrics(CacheStatistics stats)
        {
            // ゲージ値の更新はObservableMetricsで実装
            // ここでは必要に応じて追加メトリクスを記録
        }

        public static void RegisterObservableMetrics(AvroSerializerCache cache)
        {
            _meter.CreateObservableGauge<int>("avro_cache_size", () => cache.GetCachedItemCount());
            _meter.CreateObservableGauge<double>("avro_cache_hit_rate", () => cache.GetGlobalStatistics().HitRate);
        }
    }
}