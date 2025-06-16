using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace KsqlDsl.Avro
{
    public class AvroRetryPolicy
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public List<Type> RetryableExceptions { get; set; } = new()
        {
            typeof(HttpRequestException),
            typeof(TimeoutException),
            typeof(TaskCanceledException)
        };
        public List<Type> NonRetryableExceptions { get; set; } = new()
        {
            typeof(ArgumentException),
            typeof(InvalidOperationException)
        };
    }

    public class AvroOperationRetrySettings
    {
        public AvroRetryPolicy SchemaRegistration { get; set; } = new()
        {
            MaxAttempts = 5,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(60)
        };

        public AvroRetryPolicy SchemaRetrieval { get; set; } = new()
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(100),
            MaxDelay = TimeSpan.FromSeconds(10)
        };

        public AvroRetryPolicy CompatibilityCheck { get; set; } = new()
        {
            MaxAttempts = 2,
            InitialDelay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromSeconds(5)
        };

        public AvroRetryPolicy Serialization { get; set; } = new()
        {
            MaxAttempts = 1 // 再試行なし
        };
    }

    public class PerformanceThresholds
    {
        public long SlowSerializerCreationMs { get; set; } = 100;
        public double MinimumHitRate { get; set; } = 0.7;
        public int MaxCacheSize { get; set; } = 10000;
        public int CacheExpiryHours { get; set; } = 24;
    }
}