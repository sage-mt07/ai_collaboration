using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KsqlDsl.Avro
{
    public class AvroSerializerCache
    {
        private readonly ConcurrentDictionary<AvroSerializerCacheKey, ISerializer<object>> _serializers = new();
        private readonly ConcurrentDictionary<AvroSerializerCacheKey, IDeserializer<object>> _deserializers = new();
        private readonly ConcurrentDictionary<Type, EntityCacheStatus> _entityStats = new();
        private readonly ConcurrentDictionary<string, AvroSchemaInfo> _schemas = new();
        private readonly DateTime _startTime = DateTime.UtcNow;
        private readonly ILogger<AvroSerializerCache>? _logger;

        private long _totalRequests;
        private long _totalHits;
        private DateTime _lastAccess = DateTime.UtcNow;
        private DateTime? _lastClear;

        public AvroSerializerCache(ILogger<AvroSerializerCache>? logger = null)
        {
            _logger = logger;
        }

        public ISerializer<object> GetOrCreateSerializer<T>(SerializerType type, int schemaId, Func<ISerializer<object>> factory)
        {
            var key = new AvroSerializerCacheKey(typeof(T), type, schemaId);
            _lastAccess = DateTime.UtcNow;
            Interlocked.Increment(ref _totalRequests);

            if (_serializers.TryGetValue(key, out var serializer))
            {
                Interlocked.Increment(ref _totalHits);
                RecordHit<T>(type, isSerializer: true);
                return serializer;
            }

            RecordMiss<T>(type, isSerializer: true);
            var newSerializer = factory();
            _serializers[key] = newSerializer;
            return newSerializer;
        }

        public IDeserializer<object> GetOrCreateDeserializer<T>(SerializerType type, int schemaId, Func<IDeserializer<object>> factory)
        {
            var key = new AvroSerializerCacheKey(typeof(T), type, schemaId);
            _lastAccess = DateTime.UtcNow;
            Interlocked.Increment(ref _totalRequests);

            if (_deserializers.TryGetValue(key, out var deserializer))
            {
                Interlocked.Increment(ref _totalHits);
                RecordHit<T>(type, isSerializer: false);
                return deserializer;
            }

            RecordMiss<T>(type, isSerializer: false);
            var newDeserializer = factory();
            _deserializers[key] = newDeserializer;
            return newDeserializer;
        }

        public CacheStatistics GetGlobalStatistics()
        {
            return new CacheStatistics
            {
                TotalRequests = _totalRequests,
                CacheHits = _totalHits,
                CacheMisses = _totalRequests - _totalHits,
                CachedItemCount = _serializers.Count + _deserializers.Count,
                LastAccess = _lastAccess,
                LastClear = _lastClear,
                Uptime = DateTime.UtcNow - _startTime
            };
        }

        public EntityCacheStatus GetEntityCacheStatus<T>()
        {
            return GetEntityCacheStatus(typeof(T));
        }

        public EntityCacheStatus GetEntityCacheStatus(Type entityType)
        {
            return _entityStats.GetOrAdd(entityType, _ => new EntityCacheStatus { EntityType = entityType });
        }

        public Dictionary<Type, EntityCacheStatus> GetAllEntityStatuses()
        {
            return new Dictionary<Type, EntityCacheStatus>(_entityStats);
        }

        public List<AvroSchemaInfo> GetRegisteredSchemas()
        {
            return _schemas.Values.ToList();
        }

        public List<AvroSchemaInfo> GetRegisteredSchemas<T>()
        {
            return _schemas.Values.Where(s => s.EntityType == typeof(T)).ToList();
        }

        public AvroSchemaInfo? GetSchemaInfo<T>(SerializerType type)
        {
            var subject = $"{typeof(T).Name}-{type.ToString().ToLower()}";
            return _schemas.TryGetValue(subject, out var schema) ? schema : null;
        }

        public List<AvroSchemaInfo> GetSchemasBySubject(string subject)
        {
            return _schemas.Values.Where(s => s.Subject == subject).ToList();
        }

        public void RegisterSchema(AvroSchemaInfo schemaInfo)
        {
            _schemas[schemaInfo.Subject] = schemaInfo;
        }

        public void ClearCache()
        {
            _serializers.Clear();
            _deserializers.Clear();
            _entityStats.Clear();
            _schemas.Clear();
            _lastClear = DateTime.UtcNow;
            Interlocked.Exchange(ref _totalRequests, 0);
            Interlocked.Exchange(ref _totalHits, 0);
        }

        public void ClearCache<T>()
        {
            var entityType = typeof(T);
            var keysToRemove = _serializers.Keys.Where(k => k.EntityType == entityType).ToList();
            foreach (var key in keysToRemove)
            {
                _serializers.TryRemove(key, out _);
            }

            var deserializerKeysToRemove = _deserializers.Keys.Where(k => k.EntityType == entityType).ToList();
            foreach (var key in deserializerKeysToRemove)
            {
                _deserializers.TryRemove(key, out _);
            }

            _entityStats.TryRemove(entityType, out _);
        }

        public void ClearExpiredSchemas(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow - maxAge;
            var expiredSubjects = _schemas.Where(kvp => kvp.Value.LastUsed < cutoff).Select(kvp => kvp.Key).ToList();

            foreach (var subject in expiredSubjects)
            {
                _schemas.TryRemove(subject, out _);
            }
        }

        public bool RemoveSchema<T>(SerializerType type)
        {
            var subject = $"{typeof(T).Name}-{type.ToString().ToLower()}";
            return _schemas.TryRemove(subject, out _);
        }

        public CacheHealthReport GetHealthReport()
        {
            var stats = GetGlobalStatistics();
            var report = new CacheHealthReport
            {
                GeneratedAt = DateTime.UtcNow,
                GlobalStats = stats,
                EntityStats = GetAllEntityStatuses().Values.ToList()
            };

            // ヘルスチェック
            if (stats.HitRate < 0.7)
            {
                report.HealthLevel = CacheHealthLevel.Critical;
                report.Issues.Add(new CacheIssue
                {
                    Type = CacheIssueType.LowHitRate,
                    Description = $"Global hit rate is {stats.HitRate:P1}, below 70%",
                    Severity = CacheIssueSeverity.High
                });
            }
            else if (stats.HitRate < 0.9)
            {
                report.HealthLevel = CacheHealthLevel.Warning;
                report.Issues.Add(new CacheIssue
                {
                    Type = CacheIssueType.LowHitRate,
                    Description = $"Global hit rate is {stats.HitRate:P1}, below 90%",
                    Severity = CacheIssueSeverity.Medium
                });
            }
            else
            {
                report.HealthLevel = CacheHealthLevel.Healthy;
            }

            // エンティティ別チェック
            foreach (var entityStatus in report.EntityStats)
            {
                if (entityStatus.OverallHitRate < 0.5)
                {
                    report.Issues.Add(new CacheIssue
                    {
                        Type = CacheIssueType.LowHitRate,
                        Description = $"{entityStatus.EntityType.Name} hit rate is {entityStatus.OverallHitRate:P1}",
                        Severity = CacheIssueSeverity.Medium,
                        AffectedEntityType = entityStatus.EntityType
                    });
                }
            }

            GenerateRecommendations(report);
            return report;
        }

        private void RecordHit<T>(SerializerType type, bool isSerializer)
        {
            var entityType = typeof(T);
            var status = _entityStats.GetOrAdd(entityType, _ => new EntityCacheStatus { EntityType = entityType });

            if (isSerializer)
            {
                if (type == SerializerType.Key)
                    Interlocked.Increment(ref status.KeySerializerHits);
                else
                    Interlocked.Increment(ref status.ValueSerializerHits);
            }
            else
            {
                if (type == SerializerType.Key)
                    Interlocked.Increment(ref status.KeyDeserializerHits);
                else
                    Interlocked.Increment(ref status.ValueDeserializerHits);
            }
        }

        private void RecordMiss<T>(SerializerType type, bool isSerializer)
        {
            var entityType = typeof(T);
            var status = _entityStats.GetOrAdd(entityType, _ => new EntityCacheStatus { EntityType = entityType });

            if (isSerializer)
            {
                if (type == SerializerType.Key)
                    Interlocked.Increment(ref status.KeySerializerMisses);
                else
                    Interlocked.Increment(ref status.ValueSerializerMisses);
            }
            else
            {
                if (type == SerializerType.Key)
                    Interlocked.Increment(ref status.KeyDeserializerMisses);
                else
                    Interlocked.Increment(ref status.ValueDeserializerMisses);
            }
        }

        private static void GenerateRecommendations(CacheHealthReport report)
        {
            if (report.GlobalStats.HitRate < 0.7)
            {
                report.Recommendations.Add("Consider increasing cache size or warming up cache with frequently used schemas");
            }

            if (report.GlobalStats.CacheMisses > 1000)
            {
                report.Recommendations.Add("High miss count detected - verify schema registration is working correctly");
            }

            var lowPerformanceEntities = report.EntityStats.Where(e => e.OverallHitRate < 0.5).ToList();
            if (lowPerformanceEntities.Any())
            {
                var entityNames = string.Join(", ", lowPerformanceEntities.Select(e => e.EntityType.Name));
                report.Recommendations.Add($"Consider pre-loading serializers for: {entityNames}");
            }
        }

        public int GetCachedItemCount()
        {
            return _serializers.Count + _deserializers.Count;
        }
    }
}