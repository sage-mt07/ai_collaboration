using System;
using Microsoft.Extensions.Logging;

namespace KsqlDsl.Avro
{
    public static partial class AvroLogMessages
    {
        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Avro cache hit: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms, HitRate: {HitRate:P1})")]
        public static partial void CacheHit(
            ILogger logger,
            string entityType,
            string serializerType,
            int schemaId,
            long durationMs,
            double hitRate);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Warning,
            Message = "Avro cache miss: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms, HitRate: {HitRate:P1})")]
        public static partial void CacheMiss(
            ILogger logger,
            string entityType,
            string serializerType,
            int schemaId,
            long durationMs,
            double hitRate);

        [LoggerMessage(
            EventId = 2001,
            Level = LogLevel.Information,
            Message = "Schema registration succeeded: {Subject} → SchemaId {SchemaId} (Attempt: {Attempt}, Duration: {DurationMs}ms)")]
        public static partial void SchemaRegistrationSucceeded(
            ILogger logger,
            string subject,
            int schemaId,
            int attempt,
            long durationMs);

        [LoggerMessage(
            EventId = 2002,
            Level = LogLevel.Warning,
            Message = "Schema registration retry: {Subject} (Attempt: {Attempt}/{MaxAttempts}, Delay: {DelayMs}ms)")]
        public static partial void SchemaRegistrationRetry(
            ILogger logger,
            string subject,
            int attempt,
            int maxAttempts,
            long delayMs,
            Exception exception);

        [LoggerMessage(
            EventId = 2003,
            Level = LogLevel.Error,
            Message = "Schema registration failed permanently: {Subject} (Attempts: {Attempts})")]
        public static partial void SchemaRegistrationFailed(
            ILogger logger,
            string subject,
            int attempts,
            Exception exception);

        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Critical,
            Message = "Schema Registry connection failed, switching to offline mode")]
        public static partial void SchemaRegistryOfflineMode(ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Warning,
            Message = "Low cache hit rate detected: {EntityType} (HitRate: {HitRate:P1} < Threshold: {ThresholdRate:P1})")]
        public static partial void LowCacheHitRate(
            ILogger logger,
            string entityType,
            double hitRate,
            double thresholdRate);

        [LoggerMessage(
            EventId = 4002,
            Level = LogLevel.Warning,
            Message = "Slow serializer creation: {EntityType}:{SerializerType}:{SchemaId} (Duration: {DurationMs}ms > Threshold: {ThresholdMs}ms)")]
        public static partial void SlowSerializerCreation(
            ILogger logger,
            string entityType,
            string serializerType,
            int schemaId,
            long durationMs,
            long thresholdMs);

        [LoggerMessage(
            EventId = 5001,
            Level = LogLevel.Error,
            Message = "Schema compatibility check failed: {Subject} - {Reason}")]
        public static partial void SchemaCompatibilityFailed(
            ILogger logger,
            string subject,
            string reason,
            Exception? exception = null);
    }
}