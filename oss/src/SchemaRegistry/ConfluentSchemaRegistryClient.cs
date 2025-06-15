using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.SchemaRegistry;
using KsqlDsl.SchemaRegistry;

namespace KsqlDsl.SchemaRegistry.Implementation
{
    public class ConfluentSchemaRegistryClient : ISchemaRegistryClient
    {
        private readonly Confluent.SchemaRegistry.ISchemaRegistryClient _confluentClient;
        private readonly SchemaRegistryConfig _config;
        private bool _disposed = false;

        public ConfluentSchemaRegistryClient(SchemaRegistryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var confluentConfig = new Confluent.SchemaRegistry.SchemaRegistryConfig
            {
                Url = _config.Url
            };

            if (!string.IsNullOrEmpty(_config.BasicAuthUserInfo))
            {
                confluentConfig.BasicAuthUserInfo = _config.BasicAuthUserInfo;
            }

            if (_config.TimeoutMs > 0)
            {
                confluentConfig.RequestTimeoutMs = _config.TimeoutMs;
            }

            if (_config.MaxCachedSchemas > 0)
            {
                confluentConfig.MaxCachedSchemas = _config.MaxCachedSchemas;
            }

            foreach (var property in _config.Properties)
            {
                confluentConfig.Set(property.Key, property.Value);
            }

            _confluentClient = new CachedSchemaRegistryClient(confluentConfig);
        }

        // 修正理由：KafkaProducerServiceでAvroSerializerが内部Confluentクライアントが必要なため公開
        public Confluent.SchemaRegistry.ISchemaRegistryClient GetConfluentClient()
        {
            return _confluentClient;
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

            try
            {
                var schemaObj = new Schema(avroSchema, Confluent.SchemaRegistry.SchemaType.Avro);
                return await _confluentClient.RegisterSchemaAsync(subject, schemaObj);
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to register Avro schema for subject '{subject}'", ex);
            }
        }

        public async Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            try
            {
                var registeredSchema = await _confluentClient.GetLatestSchemaAsync(subject);

                if (registeredSchema.SchemaType != Confluent.SchemaRegistry.SchemaType.Avro)
                {
                    throw new SchemaRegistryOperationException($"Subject '{subject}' contains non-Avro schema. KsqlDsl supports Avro schemas only.");
                }

                return new AvroSchemaInfo
                {
                    Id = registeredSchema.Id,
                    Version = registeredSchema.Version,
                    Subject = subject,
                    AvroSchema = registeredSchema.SchemaString
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get latest Avro schema for subject '{subject}'", ex);
            }
        }

        public async Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId)
        {
            if (schemaId <= 0)
                throw new ArgumentException("Schema ID must be positive", nameof(schemaId));

            try
            {
                var schema = await _confluentClient.GetSchemaAsync(schemaId);

                if (schema.SchemaType != Confluent.SchemaRegistry.SchemaType.Avro)
                {
                    throw new SchemaRegistryOperationException($"Schema with ID '{schemaId}' is not Avro format. KsqlDsl supports Avro schemas only.");
                }

                return new AvroSchemaInfo
                {
                    Id = schemaId,
                    Version = -1,
                    Subject = string.Empty,
                    AvroSchema = schema.SchemaString
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get Avro schema with ID '{schemaId}'", ex);
            }
        }

        public async Task<bool> CheckCompatibilityAsync(string subject, string avroSchema)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(avroSchema))
                throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

            try
            {
                var schemaObj = new Schema(avroSchema, Confluent.SchemaRegistry.SchemaType.Avro);
                return await _confluentClient.IsCompatibleAsync(subject, schemaObj);
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to check Avro schema compatibility for subject '{subject}'", ex);
            }
        }

        public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            try
            {
                return await _confluentClient.GetSubjectVersionsAsync(subject);
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get Avro schema versions for subject '{subject}'", ex);
            }
        }

        public async Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (version <= 0)
                throw new ArgumentException("Version must be positive", nameof(version));

            try
            {
                var registeredSchema = await _confluentClient.GetRegisteredSchemaAsync(subject, version);

                if (registeredSchema.SchemaType != Confluent.SchemaRegistry.SchemaType.Avro)
                {
                    throw new SchemaRegistryOperationException($"Subject '{subject}' version {version} contains non-Avro schema. KsqlDsl supports Avro schemas only.");
                }

                return new AvroSchemaInfo
                {
                    Id = registeredSchema.Id,
                    Version = registeredSchema.Version,
                    Subject = subject,
                    AvroSchema = registeredSchema.SchemaString
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get Avro schema for subject '{subject}' version {version}", ex);
            }
        }

        public async Task<IList<string>> GetAllSubjectsAsync()
        {
            try
            {
                return await _confluentClient.GetAllSubjectsAsync();
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException("Failed to get all subjects", ex);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _confluentClient?.Dispose();
                _disposed = true;
            }
        }
    }

    public class SchemaRegistryOperationException : Exception
    {
        public SchemaRegistryOperationException(string message) : base(message) { }
        public SchemaRegistryOperationException(string message, Exception innerException) : base(message, innerException) { }
    }
}