using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry.Implementation
{
    public class ConfluentSchemaRegistryClient : ISchemaRegistryClient
    {
        private readonly SchemaRegistryConfig _config;
        private readonly Dictionary<string, AvroSchemaInfo> _schemas = new();
        private readonly Dictionary<int, AvroSchemaInfo> _schemasById = new();
        private readonly Dictionary<string, List<int>> _subjectVersions = new();
        private int _nextSchemaId = 1;
        private bool _disposed = false;

        public ConfluentSchemaRegistryClient(SchemaRegistryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public async Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema)
        {
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(keySchema))
                throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));
            if (string.IsNullOrEmpty(valueSchema))
                throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

            var keySchemaId = await RegisterKeySchemaAsync(topicName, keySchema);
            var valueSchemaId = await RegisterValueSchemaAsync(topicName, valueSchema);

            return (keySchemaId, valueSchemaId);
        }

        public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
        {
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(keySchema))
                throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));

            var subject = $"{topicName}-key";
            return await RegisterSchemaAsync(subject, keySchema);
        }

        public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
        {
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(valueSchema))
                throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

            var subject = $"{topicName}-value";
            return await RegisterSchemaAsync(subject, valueSchema);
        }

        public async Task<int> RegisterSchemaAsync(string subject, string avroSchema)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(avroSchema))
                throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

            await Task.Delay(1); // Simulate async operation

            var schemaId = _nextSchemaId++;
            var version = GetNextVersion(subject);

            var schemaInfo = new AvroSchemaInfo
            {
                Id = schemaId,
                Version = version,
                Subject = subject,
                AvroSchema = avroSchema
            };

            _schemas[subject] = schemaInfo;
            _schemasById[schemaId] = schemaInfo;

            if (!_subjectVersions.ContainsKey(subject))
                _subjectVersions[subject] = new List<int>();
            _subjectVersions[subject].Add(version);

            return schemaId;
        }

        public async Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            await Task.Delay(1); // Simulate async operation

            if (_schemas.TryGetValue(subject, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Subject '{subject}' not found");
        }

        public async Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId)
        {
            if (schemaId <= 0)
                throw new ArgumentException("Schema ID must be positive", nameof(schemaId));

            await Task.Delay(1); // Simulate async operation

            if (_schemasById.TryGetValue(schemaId, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Schema with ID '{schemaId}' not found");
        }

        public async Task<bool> CheckCompatibilityAsync(string subject, string avroSchema)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(avroSchema))
                throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

            await Task.Delay(1); // Simulate async operation

            // Simple implementation: always compatible if subject exists
            return _schemas.ContainsKey(subject);
        }

        public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            await Task.Delay(1); // Simulate async operation

            if (_subjectVersions.TryGetValue(subject, out var versions))
                return versions;

            return new List<int>();
        }

        public async Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (version <= 0)
                throw new ArgumentException("Version must be positive", nameof(version));

            await Task.Delay(1); // Simulate async operation

            if (_schemas.TryGetValue(subject, out var schema) && schema.Version == version)
                return schema;

            throw new SchemaRegistryOperationException($"Schema for subject '{subject}' version {version} not found");
        }

        public async Task<IList<string>> GetAllSubjectsAsync()
        {
            await Task.Delay(1); // Simulate async operation
            return new List<string>(_schemas.Keys);
        }

        private int GetNextVersion(string subject)
        {
            if (_subjectVersions.TryGetValue(subject, out var versions) && versions.Count > 0)
                return versions.Max() + 1;
            return 1;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _schemas.Clear();
                _schemasById.Clear();
                _subjectVersions.Clear();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}