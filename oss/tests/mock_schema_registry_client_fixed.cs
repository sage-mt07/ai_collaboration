using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.SchemaRegistry.Implementation;

namespace KsqlDsl.Tests.SchemaRegistry
{
    /// <summary>
    /// Mock schema registry client for testing (Avro schemas only)
    /// KsqlDsl supports Avro format exclusively
    /// </summary>
    public class MockSchemaRegistryClient : ISchemaRegistryClient
    {
        private readonly Dictionary<string, AvroSchemaInfo> _schemas = new();
        private readonly Dictionary<int, AvroSchemaInfo> _schemasById = new();
        private readonly Dictionary<string, List<int>> _subjectVersions = new();
        private int _nextSchemaId = 1;
        private bool _disposed = false;

        public async Task<int> RegisterSchemaAsync(string subject, string avroSchema)
        {
            // Add proper argument validation
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

        public async Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(keySchema))
                throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));
            if (string.IsNullOrEmpty(valueSchema))
                throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

            await Task.Delay(1); // Simulate async operation

            var keySchemaId = await RegisterKeySchemaAsync(topicName, keySchema);
            var valueSchemaId = await RegisterValueSchemaAsync(topicName, valueSchema);

            return (keySchemaId, valueSchemaId);
        }

        public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(keySchema))
                throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));

            await Task.Delay(1); // Simulate async operation

            var subject = $"{topicName}-key";
            return await RegisterSchemaAsync(subject, keySchema);
        }

        public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(valueSchema))
                throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

            await Task.Delay(1); // Simulate async operation

            var subject = $"{topicName}-value";
            return await RegisterSchemaAsync(subject, valueSchema);
        }

        public async Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            await Task.Delay(1); // Simulate async operation

            if (_schemas.TryGetValue(subject, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Subject '{subject}' not found");
        }

        public async Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId)
        {
            // Add proper argument validation for invalid IDs
            if (schemaId <= 0)
                throw new ArgumentException("Schema ID must be positive", nameof(schemaId));

            await Task.Delay(1); // Simulate async operation

            if (_schemasById.TryGetValue(schemaId, out var schema))
                return schema;

            throw new SchemaRegistryOperationException($"Schema with ID '{schemaId}' not found");
        }

        public async Task<bool> CheckCompatibilityAsync(string subject, string avroSchema)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(avroSchema))
                throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

            await Task.Delay(1); // Simulate async operation

            // Simple mock: always compatible if subject exists
            return _schemas.ContainsKey(subject);
        }

        public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
        {
            // Add proper argument validation
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            await Task.Delay(1); // Simulate async operation

            if (_subjectVersions.TryGetValue(subject, out var versions))
                return versions;

            return new List<int>();
        }

        public async Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version)
        {
            // Add proper argument validation
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
            return _schemas.Keys.ToList();
        }

        private int GetNextVersion(string subject)
        {
            if (_subjectVersions.TryGetValue(subject, out var versions))
                return versions.Max() + 1;
            return 1;
        }

        public void Dispose()
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}