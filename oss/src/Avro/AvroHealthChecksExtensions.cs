using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KsqlDsl.Avro
{
    public static class AvroHealthChecksExtensions
    {
        public static IServiceCollection AddAvroHealthChecks(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddHealthChecks()
                .AddCheck<AvroSerializerCacheHealthCheck>("avro-cache")
                .AddCheck<SchemaRegistryHealthCheck>("schema-registry")
                .AddCheck<AvroCompatibilityHealthCheck>("avro-compatibility");

            return services;
        }
    }

    public class AvroSerializerCacheHealthCheck : IHealthCheck
    {
        private readonly AvroSerializerCache _cache;
        private readonly IOptions<PerformanceThresholds> _thresholds;

        public AvroSerializerCacheHealthCheck(
            AvroSerializerCache cache,
            IOptions<PerformanceThresholds> thresholds)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(1, cancellationToken);

            var stats = _cache.GetGlobalStatistics();
            var data = new Dictionary<string, object>
            {
                ["hit_rate"] = stats.HitRate,
                ["total_requests"] = stats.TotalRequests,
                ["cached_items"] = stats.CachedItemCount,
                ["uptime_minutes"] = stats.Uptime.TotalMinutes
            };

            if (stats.HitRate < _thresholds.Value.MinimumHitRate)
            {
                return HealthCheckResult.Degraded(
                    $"Cache hit rate is {stats.HitRate:P1}, below threshold {_thresholds.Value.MinimumHitRate:P1}",
                    data: data);
            }

            if (stats.CachedItemCount > _thresholds.Value.MaxCacheSize)
            {
                return HealthCheckResult.Degraded(
                    $"Cache size {stats.CachedItemCount} exceeds maximum {_thresholds.Value.MaxCacheSize}",
                    data: data);
            }

            return HealthCheckResult.Healthy("Avro cache is performing well", data);
        }
    }

    public class SchemaRegistryHealthCheck : IHealthCheck
    {
        private readonly ISchemaRegistryClient _schemaRegistryClient;
        private readonly ILogger<SchemaRegistryHealthCheck> _logger;

        public SchemaRegistryHealthCheck(
            ISchemaRegistryClient schemaRegistryClient,
            ILogger<SchemaRegistryHealthCheck> logger)
        {
            _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var activity = AvroActivitySource.StartCacheOperation("health_check", "schema_registry");
                var stopwatch = Stopwatch.StartNew();

                var subjects = await _schemaRegistryClient.GetAllSubjectsAsync();
                stopwatch.Stop();

                var data = new Dictionary<string, object>
                {
                    ["response_time_ms"] = stopwatch.ElapsedMilliseconds,
                    ["subject_count"] = subjects.Count
                };

                if (stopwatch.ElapsedMilliseconds > 5000)
                {
                    return HealthCheckResult.Degraded(
                        $"Schema Registry response time is slow: {stopwatch.ElapsedMilliseconds}ms",
                        data: data);
                }

                return HealthCheckResult.Healthy("Schema Registry is accessible", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Schema Registry health check failed");
                return HealthCheckResult.Unhealthy("Schema Registry is not accessible", ex);
            }
        }
    }

    public class AvroCompatibilityHealthCheck : IHealthCheck
    {
        private readonly SchemaVersionManager _versionManager;
        private readonly ILogger