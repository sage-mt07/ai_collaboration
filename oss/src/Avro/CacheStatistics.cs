using System;
using System.Collections.Generic;

namespace KsqlDsl.Avro
{
    public class CacheStatistics
    {
        public long TotalRequests { get; set; }
        public long CacheHits { get; set; }
        public long CacheMisses { get; set; }
        public double HitRate => TotalRequests > 0 ? (double)CacheHits / TotalRequests : 0.0;
        public int CachedItemCount { get; set; }
        public DateTime LastAccess { get; set; }
        public DateTime? LastClear { get; set; }
        public TimeSpan Uptime { get; set; }
    }

    public class EntityCacheStatus
    {
        public Type EntityType { get; set; } = null!;
        public long KeySerializerHits { get; set; }
        public long KeySerializerMisses { get; set; }
        public long ValueSerializerHits { get; set; }
        public long ValueSerializerMisses { get; set; }
        public long KeyDeserializerHits { get; set; }
        public long KeyDeserializerMisses { get; set; }
        public long ValueDeserializerHits { get; set; }
        public long ValueDeserializerMisses { get; set; }

        public double KeySerializerHitRate => GetHitRate(KeySerializerHits, KeySerializerMisses);
        public double ValueSerializerHitRate => GetHitRate(ValueSerializerHits, ValueSerializerMisses);
        public double KeyDeserializerHitRate => GetHitRate(KeyDeserializerHits, KeyDeserializerMisses);
        public double ValueDeserializerHitRate => GetHitRate(ValueDeserializerHits, ValueDeserializerMisses);
        public double OverallHitRate => GetHitRate(AllHits, AllMisses);

        private long AllHits => KeySerializerHits + ValueSerializerHits + KeyDeserializerHits + ValueDeserializerHits;
        private long AllMisses => KeySerializerMisses + ValueSerializerMisses + KeyDeserializerMisses + ValueDeserializerMisses;

        private static double GetHitRate(long hits, long misses)
        {
            var total = hits + misses;
            return total > 0 ? (double)hits / total : 0.0;
        }
    }

    public class AvroSchemaInfo
    {
        public Type EntityType { get; set; } = null!;
        public SerializerType Type { get; set; }
        public int SchemaId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public DateTime LastUsed { get; set; }
        public long UsageCount { get; set; }
        public string SchemaJson { get; set; } = string.Empty;
        public int Version { get; set; }
    }

    public class CacheHealthReport
    {
        public DateTime GeneratedAt { get; set; }
        public CacheStatistics GlobalStats { get; set; } = null!;
        public List<EntityCacheStatus> EntityStats { get; set; } = new();
        public List<CacheIssue> Issues { get; set; } = new();
        public CacheHealthLevel HealthLevel { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }

    public class CacheIssue
    {
        public CacheIssueType Type { get; set; }
        public string Description { get; set; } = string.Empty;
        public CacheIssueSeverity Severity { get; set; }
        public Type? AffectedEntityType { get; set; }
        public string? Recommendation { get; set; }
    }

    public enum CacheIssueType
    {
        LowHitRate,
        ExcessiveMisses,
        StaleSchemas,
        MemoryPressure,
        SchemaVersionMismatch
    }

    public enum CacheIssueSeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum CacheHealthLevel
    {
        Healthy,
        Warning,
        Critical
    }
}