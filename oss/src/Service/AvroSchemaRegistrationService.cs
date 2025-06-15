using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KsqlDsl.Attributes;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.Validation;

namespace KsqlDsl.Services
{
    public class AvroSchemaRegistrationService
    {
        private readonly ISchemaRegistryClient? _schemaRegistryClient;
        private readonly ValidationMode _validationMode;
        private readonly bool _enableDebugLogging;

        public AvroSchemaRegistrationService(
            ISchemaRegistryClient? schemaRegistryClient,
            ValidationMode validationMode,
            bool enableDebugLogging = false)
        {
            _schemaRegistryClient = schemaRegistryClient;
            _validationMode = validationMode;
            _enableDebugLogging = enableDebugLogging;
        }

        public async Task RegisterAllSchemasAsync(Dictionary<Type, EntityModel> entityModels)
        {
            if (_schemaRegistryClient == null)
            {
                if (_enableDebugLogging)
                    Console.WriteLine("[DEBUG] Schema Registry client not configured, skipping schema registration");
                return;
            }

            if (_enableDebugLogging)
                Console.WriteLine($"[DEBUG] Starting Avro schema registration for {entityModels.Count} entities");

            var registrationTasks = new List<Task>();

            foreach (var kvp in entityModels)
            {
                var entityType = kvp.Key;
                var entityModel = kvp.Value;

                if (!entityModel.IsValid)
                {
                    if (_validationMode == ValidationMode.Strict)
                        throw new InvalidOperationException($"Cannot register schema for invalid entity model: {entityType.Name}");

                    if (_enableDebugLogging)
                        Console.WriteLine($"[DEBUG] Skipping schema registration for invalid entity: {entityType.Name}");
                    continue;
                }

                var task = RegisterEntitySchemaAsync(entityType, entityModel);
                registrationTasks.Add(task);
            }

            await Task.WhenAll(registrationTasks);

            if (_enableDebugLogging)
                Console.WriteLine($"[DEBUG] Completed Avro schema registration for all entities");
        }

        private async Task RegisterEntitySchemaAsync(Type entityType, EntityModel entityModel)
        {
            try
            {
                var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;

                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Registering Avro schemas for {entityType.Name} → Topic: {topicName}");

                var keySchemaId = await RegisterKeySchemaAsync(entityType, entityModel, topicName);
                var valueSchemaId = await RegisterValueSchemaAsync(entityType, topicName);

                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Successfully registered schemas for {entityType.Name}: Key={keySchemaId}, Value={valueSchemaId}");
            }
            catch (Exception ex)
            {
                if (_validationMode == ValidationMode.Strict)
                    throw new InvalidOperationException($"Failed to register Avro schemas for entity {entityType.Name}: {ex.Message}", ex);

                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Schema registration failed for {entityType.Name}: {ex.Message}");
            }
        }

        private async Task<int> RegisterKeySchemaAsync(Type entityType, EntityModel entityModel, string topicName)
        {
            var keyProperties = entityModel.KeyProperties;

            string keySchema;
            if (keyProperties.Length == 0)
            {
                keySchema = SchemaGenerator.GenerateKeySchema<string>();
                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] No key properties found for {entityType.Name}, using default string key");
            }
            else if (keyProperties.Length == 1)
            {
                var keyProperty = keyProperties[0];
                keySchema = SchemaGenerator.GenerateKeySchema(keyProperty.PropertyType);
                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Single key property: {keyProperty.Name} ({keyProperty.PropertyType.Name})");
            }
            else
            {
                var compositeKeyType = CreateCompositeKeyType(keyProperties);
                keySchema = SchemaGenerator.GenerateKeySchema(compositeKeyType);
                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Composite key with {keyProperties.Length} properties");
            }

            return await _schemaRegistryClient!.RegisterKeySchemaAsync(topicName, keySchema);
        }

        private async Task<int> RegisterValueSchemaAsync(Type entityType, string topicName)
        {
            var valueSchema = SchemaGenerator.GenerateSchema(entityType, new SchemaGenerationOptions
            {
                CustomName = $"{topicName}_value",
                Namespace = $"{entityType.Namespace}.Avro",
                Documentation = $"Avro schema for {entityType.Name} topic: {topicName}",
                PrettyFormat = false
            });

            return await _schemaRegistryClient!.RegisterValueSchemaAsync(topicName, valueSchema);
        }

        private Type CreateCompositeKeyType(System.Reflection.PropertyInfo[] keyProperties)
        {
            return typeof(Dictionary<string, object>);
        }

        public async Task<List<string>> GetRegisteredSchemasAsync()
        {
            if (_schemaRegistryClient == null)
                return new List<string>();

            try
            {
                var subjects = await _schemaRegistryClient.GetAllSubjectsAsync();
                return subjects.ToList();
            }
            catch (Exception ex)
            {
                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Failed to get registered schemas: {ex.Message}");
                return new List<string>();
            }
        }

        public async Task<bool> CheckSchemaCompatibilityAsync(string subject, string schema)
        {
            if (_schemaRegistryClient == null)
                return false;

            try
            {
                return await _schemaRegistryClient.CheckCompatibilityAsync(subject, schema);
            }
            catch (Exception ex)
            {
                if (_enableDebugLogging)
                    Console.WriteLine($"[DEBUG] Schema compatibility check failed for {subject}: {ex.Message}");
                return false;
            }
        }
    }
}