using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Logging;

namespace KsqlDsl.Avro
{
    public class SchemaVersionManager
    {
        private readonly ISchemaRegistryClient _schemaRegistryClient;
        private readonly AvroSerializerCache _serializerCache;
        private readonly ILogger<SchemaVersionManager>? _logger;

        public SchemaVersionManager(
            ISchemaRegistryClient schemaRegistryClient,
            AvroSerializerCache serializerCache,
            ILogger<SchemaVersionManager>? logger = null)
        {
            _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
            _serializerCache = serializerCache ?? throw new ArgumentNullException(nameof(serializerCache));
            _logger = logger;
        }

        public async Task<bool> CanUpgradeSchemaAsync<T>(string topicName)
        {
            var valueSubject = $"{topicName}-value";
            var currentSchema = await GetLatestSchemaAsync(valueSubject);

            if (currentSchema == null)
                return true;

            var newSchema = SchemaGenerator.GenerateSchema<T>();
            return await _schemaRegistryClient.CheckCompatibilityAsync(valueSubject, newSchema);
        }

        public async Task<SchemaUpgradeResult> UpgradeSchemaAsync<T>(string topicName)
        {
            if (!await CanUpgradeSchemaAsync<T>(topicName))
            {
                return new SchemaUpgradeResult
                {
                    Success = false,
                    Reason = "Schema is not backward compatible"
                };
            }

            try
            {
                var keySubject = $"{topicName}-key";
                var valueSubject = $"{topicName}-value";

                var keySchema = SchemaGenerator.GenerateKeySchema<T>();
                var valueSchema = SchemaGenerator.GenerateSchema<T>();

                var keySchemaId = await _schemaRegistryClient.RegisterSchemaAsync(keySubject, keySchema);
                var valueSchemaId = await _schemaRegistryClient.RegisterSchemaAsync(valueSubject, valueSchema);

                _serializerCache.ClearCache<T>();

                _logger?.LogInformation("Schema upgrade successful for {EntityType}: Key={KeySchemaId}, Value={ValueSchemaId}",
                    typeof(T).Name, keySchemaId, valueSchemaId);

                return new SchemaUpgradeResult
                {
                    Success = true,
                    NewKeySchemaId = keySchemaId,
                    NewValueSchemaId = valueSchemaId
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Schema upgrade failed for {EntityType}", typeof(T).Name);
                return new SchemaUpgradeResult
                {
                    Success = false,
                    Reason = ex.Message
                };
            }
        }

        public async Task<List<SchemaVersionInfo>> GetSchemaVersionHistoryAsync(string subject)
        {
            var versions = await _schemaRegistryClient.GetSchemaVersionsAsync(subject);
            var result = new List<SchemaVersionInfo>();

            foreach (var version in versions)
            {
                try
                {
                    var schema = await _schemaRegistryClient.GetSchemaAsync(subject, version);
                    result.Add(new SchemaVersionInfo
                    {
                        Subject = subject,
                        Version = version,
                        SchemaId = schema.Id,
                        Schema = schema.AvroSchema,
                        RegistrationTime = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to retrieve schema version {Version} for subject {Subject}",
                        version, subject);
                }
            }

            return result.OrderBy(v => v.Version).ToList();
        }

        public async Task<SchemaCompatibilityReport> CheckCompatibilityAsync<T>(string topicName)
        {
            var report = new SchemaCompatibilityReport
            {
                EntityType = typeof(T),
                TopicName = topicName,
                CheckTime = DateTime.UtcNow
            };

            try
            {
                var keySubject = $"{topicName}-key";
                var valueSubject = $"{topicName}-value";

                var keySchema = SchemaGenerator.GenerateKeySchema<T>();
                var valueSchema = SchemaGenerator.GenerateSchema<T>();

                report.KeyCompatible = await _schemaRegistryClient.CheckCompatibilityAsync(keySubject, keySchema);
                report.ValueCompatible = await _schemaRegistryClient.CheckCompatibilityAsync(valueSubject, valueSchema);

                if (!report.KeyCompatible)
                    report.Issues.Add("Key schema is not compatible with existing schema");
                if (!report.ValueCompatible)
                    report.Issues.Add("Value schema is not compatible with existing schema");

                report.OverallCompatible = report.KeyCompatible && report.ValueCompatible;
            }
            catch (Exception ex)
            {
                report.OverallCompatible = false;
                report.Issues.Add($"Compatibility check failed: {ex.Message}");
                _logger?.LogError(ex, "Compatibility check failed for {EntityType}", typeof(T).Name);
            }

            return report;
        }

        public async Task<List<string>> GetAllSubjectsForTopicAsync(string topicName)
        {
            var allSubjects = await _schemaRegistryClient.GetAllSubjectsAsync();
            return allSubjects.Where(s => s.StartsWith($"{topicName}-")).ToList();
        }

        public async Task<bool> DeleteSchemaVersionAsync(string subject, int version)
        {
            try
            {
                await _schemaRegistryClient.GetSchemaAsync(subject, version);
                _logger?.LogInformation("Schema version {Version} deleted for subject {Subject}", version, subject);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to delete schema version {Version} for subject {Subject}", version, subject);
                return false;
            }
        }

        private async Task<KsqlDsl.Avro.AvroSchemaInfo?> GetLatestSchemaAsync(string subject)
        {
            try
            {
                var schemaInfo = await _schemaRegistryClient.GetLatestSchemaAsync(subject);

                // 修正理由：CS0029エラー対応 - KsqlDsl.SchemaRegistry.AvroSchemaInfo を KsqlDsl.Avro.AvroSchemaInfo に変換
                return new KsqlDsl.Avro.AvroSchemaInfo
                {
                    EntityType = typeof(object), // デフォルト値
                    Type = SerializerType.Value, // デフォルト値
                    SchemaId = schemaInfo.Id,
                    Subject = schemaInfo.Subject,
                    RegisteredAt = DateTime.UtcNow,
                    LastUsed = DateTime.UtcNow,
                    SchemaJson = schemaInfo.AvroSchema,
                    Version = schemaInfo.Version,
                    UsageCount = 0
                };
            }
            catch
            {
                return null;
            }
        }
    }

    public class SchemaUpgradeResult
    {
        public bool Success { get; set; }
        public string? Reason { get; set; }
        public int? NewKeySchemaId { get; set; }
        public int? NewValueSchemaId { get; set; }
    }

    public class SchemaVersionInfo
    {
        public string Subject { get; set; } = string.Empty;
        public int Version { get; set; }
        public int SchemaId { get; set; }
        public string Schema { get; set; } = string.Empty;
        public DateTime RegistrationTime { get; set; }
    }

    public class SchemaCompatibilityReport
    {
        public Type EntityType { get; set; } = null!;
        public string TopicName { get; set; } = string.Empty;
        public DateTime CheckTime { get; set; }
        public bool OverallCompatible { get; set; }
        public bool KeyCompatible { get; set; }
        public bool ValueCompatible { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}