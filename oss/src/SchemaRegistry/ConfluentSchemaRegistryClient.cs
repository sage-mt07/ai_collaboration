using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Confluent.SchemaRegistry;
using KsqlDsl.SchemaRegistry;

namespace KsqlDsl.SchemaRegistry.Implementation
{
    /// <summary>
    /// Confluent Schema Registry client implementation (Avro schemas only)
    /// </summary>
    public class ConfluentSchemaRegistryClient : ISchemaRegistryClient
    {
        private readonly Confluent.SchemaRegistry.ISchemaRegistryClient _confluentClient;
        private readonly SchemaRegistryConfig _config;
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of ConfluentSchemaRegistryClient
        /// </summary>
        /// <param name="config">Schema registry configuration</param>
        public ConfluentSchemaRegistryClient(SchemaRegistryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            // Configure Confluent Schema Registry client
            var confluentConfig = new Confluent.SchemaRegistry.SchemaRegistryConfig
            {
                Url = _config.Url
            };

            // Set basic auth if provided
            if (!string.IsNullOrEmpty(_config.BasicAuthUserInfo))
            {
                confluentConfig.BasicAuthUserInfo = _config.BasicAuthUserInfo;
            }

            // Set timeout if specified
            if (_config.TimeoutMs > 0)
            {
                confluentConfig.RequestTimeoutMs = _config.TimeoutMs;
            }

            // Set max cached schemas if specified  
            if (_config.MaxCachedSchemas > 0)
            {
                confluentConfig.MaxCachedSchemas = _config.MaxCachedSchemas;
            }

            // Add additional properties
            foreach (var property in _config.Properties)
            {
                confluentConfig.Set(property.Key, property.Value);
            }

            // Create the Confluent client
            _confluentClient = new CachedSchemaRegistryClient(confluentConfig);
        }

        /// <summary>
        /// Registers both key and value schemas for the specified topic
        /// </summary>
        /// <param name="topicName">The topic name</param>
        /// <param name="keySchema">The key schema (Avro format)</param>
        /// <param name="valueSchema">The value schema (Avro format)</param>
        /// <returns>Tuple of (keySchemaId, valueSchemaId)</returns>
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

        /// <summary>
        /// Registers a key schema for the specified topic
        /// </summary>
        /// <param name="topicName">The topic name</param>
        /// <param name="keySchema">The key schema (Avro format)</param>
        /// <returns>The key schema ID</returns>
        public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
        {
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(keySchema))
                throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));

            var subject = $"{topicName}-key";
            return await RegisterSchemaAsync(subject, keySchema);
        }

        /// <summary>
        /// Registers a value schema for the specified topic
        /// </summary>
        /// <param name="topicName">The topic name</param>
        /// <param name="valueSchema">The value schema (Avro format)</param>
        /// <returns>The value schema ID</returns>
        public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
        {
            if (string.IsNullOrEmpty(topicName))
                throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
            if (string.IsNullOrEmpty(valueSchema))
                throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

            var subject = $"{topicName}-value";
            return await RegisterSchemaAsync(subject, valueSchema);
        }

        /// <summary>
        /// Registers a new schema for the specified subject (legacy method)
        /// </summary>
        /// <param name="subject">The subject name</param>
        /// <param name="schema">The schema to register</param>
        /// <returns>The schema ID assigned by the registry</returns>
        public async Task<int> RegisterSchemaAsync(string subject, string schema)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(schema))
                throw new ArgumentException("Schema cannot be null or empty", nameof(schema));

            try
            {
                // Create Schema object with Avro type
                var schemaObj = new Schema(schema, Confluent.SchemaRegistry.SchemaType.Avro);
                return await _confluentClient.RegisterSchemaAsync(subject, schemaObj);
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to register schema for subject '{subject}'", ex);
            }
        }

        /// <summary>
        /// Gets the latest schema for the specified subject
        /// </summary>
        /// <param name="subject">The subject name</param>
        /// <returns>The latest schema information</returns>
        public async Task<SchemaInfo> GetLatestSchemaAsync(string subject)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

            try
            {
                var registeredSchema = await _confluentClient.GetLatestSchemaAsync(subject);
                return new SchemaInfo
                {
                    Id = registeredSchema.Id,
                    Version = registeredSchema.Version,
                    Subject = subject,
                    Schema = registeredSchema.SchemaString,
                    SchemaType = ConvertSchemaType(registeredSchema.SchemaType)
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get latest schema for subject '{subject}'", ex);
            }
        }

        /// <summary>
        /// Gets a specific schema by ID
        /// </summary>
        /// <param name="schemaId">The schema ID</param>
        /// <returns>The schema information</returns>
        public async Task<SchemaInfo> GetSchemaByIdAsync(int schemaId)
        {
            if (schemaId <= 0)
                throw new ArgumentException("Schema ID must be positive", nameof(schemaId));

            try
            {
                var schema = await _confluentClient.GetSchemaAsync(schemaId);
                return new SchemaInfo
                {
                    Id = schemaId,
                    Version = -1, // Version not available when getting by ID
                    Subject = string.Empty, // Subject not available when getting by ID
                    Schema = schema.SchemaString,
                    SchemaType = ConvertSchemaType(schema.SchemaType)
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get schema with ID '{schemaId}'", ex);
            }
        }

        /// <summary>
        /// Checks if a schema is compatible with the latest version of the subject
        /// </summary>
        /// <param name="subject">The subject name</param>
        /// <param name="schema">The schema to check</param>
        /// <returns>True if compatible, false otherwise</returns>
        public async Task<bool> CheckCompatibilityAsync(string subject, string schema)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (string.IsNullOrEmpty(schema))
                throw new ArgumentException("Schema cannot be null or empty", nameof(schema));

            try
            {
                var schemaObj = new Schema(schema, Confluent.SchemaRegistry.SchemaType.Avro);
                return await _confluentClient.IsCompatibleAsync(subject, schemaObj);
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to check compatibility for subject '{subject}'", ex);
            }
        }

        /// <summary>
        /// Gets all versions of a schema for the specified subject
        /// </summary>
        /// <param name="subject">The subject name</param>
        /// <returns>List of version numbers</returns>
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
                throw new SchemaRegistryOperationException($"Failed to get schema versions for subject '{subject}'", ex);
            }
        }

        /// <summary>
        /// Gets a specific version of a schema for the specified subject
        /// </summary>
        /// <param name="subject">The subject name</param>
        /// <param name="version">The version number</param>
        /// <returns>The schema information</returns>
        public async Task<SchemaInfo> GetSchemaAsync(string subject, int version)
        {
            if (string.IsNullOrEmpty(subject))
                throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
            if (version <= 0)
                throw new ArgumentException("Version must be positive", nameof(version));

            try
            {
                var registeredSchema = await _confluentClient.GetRegisteredSchemaAsync(subject, version);
                return new SchemaInfo
                {
                    Id = registeredSchema.Id,
                    Version = registeredSchema.Version,
                    Subject = subject,
                    Schema = registeredSchema.SchemaString,
                    SchemaType = ConvertSchemaType(registeredSchema.SchemaType)
                };
            }
            catch (SchemaRegistryException ex)
            {
                throw new SchemaRegistryOperationException($"Failed to get schema for subject '{subject}' version {version}", ex);
            }
        }


        /// <summary>
        /// Gets all subjects registered in the schema registry
        /// </summary>
        /// <returns>List of subject names</returns>
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

        /// <summary>
        /// Converts Confluent schema type to KsqlDsl schema type (Avro fixed)
        /// </summary>
        /// <param name="confluentType">Confluent schema type</param>
        /// <returns>KsqlDsl schema type (always Avro)</returns>
        private static KsqlDsl.SchemaRegistry.SchemaType ConvertSchemaType(Confluent.SchemaRegistry.SchemaType confluentType)
        {
            // KsqlDsl only supports Avro schemas
            return KsqlDsl.SchemaRegistry.SchemaType.Avro;
        }

        /// <summary>
        /// Disposes the client resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the client resources
        /// </summary>
        /// <param name="disposing">True if disposing, false if finalizing</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _confluentClient?.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Exception thrown when schema registry operations fail
    /// </summary>
    public class SchemaRegistryOperationException : Exception
    {
        public SchemaRegistryOperationException(string message) : base(message) { }
        public SchemaRegistryOperationException(string message, Exception innerException) : base(message, innerException) { }
    }
}