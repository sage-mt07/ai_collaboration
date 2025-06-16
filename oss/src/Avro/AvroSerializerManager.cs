using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Logging;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

namespace KsqlDsl.Avro
{
    public class AvroSerializerManager
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _schemaRegistryClient;
        private readonly AvroSerializerCache _cache;
        private readonly ILogger<AvroSerializerManager>? _logger;

        public AvroSerializerManager(
            ConfluentSchemaRegistry.ISchemaRegistryClient schemaRegistryClient,
            AvroSerializerCache cache,
            ILogger<AvroSerializerManager>? logger = null)
        {
            _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger;
        }

        public async Task<(ISerializer<object>, ISerializer<object>)> CreateSerializersAsync<T>(EntityModel entityModel)
        {
            var keySchemaId = await RegisterOrGetKeySchemaIdAsync<T>(entityModel);
            var valueSchemaId = await RegisterOrGetValueSchemaIdAsync<T>(entityModel);

            var keySerializer = CreateKeySerializer<T>(entityModel, keySchemaId);
            var valueSerializer = CreateValueSerializer<T>(valueSchemaId);

            return (keySerializer, valueSerializer);
        }

        public async Task<(IDeserializer<object>, IDeserializer<object>)> CreateDeserializersAsync<T>(EntityModel entityModel)
        {
            var keySchemaId = await RegisterOrGetKeySchemaIdAsync<T>(entityModel);
            var valueSchemaId = await RegisterOrGetValueSchemaIdAsync<T>(entityModel);

            var keyDeserializer = CreateKeyDeserializer<T>(entityModel, keySchemaId);
            var valueDeserializer = CreateValueDeserializer<T>(valueSchemaId);

            return (keyDeserializer, valueDeserializer);
        }

        private async Task<int> RegisterOrGetKeySchemaIdAsync<T>(EntityModel entityModel)
        {
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
            var keyType = KeyExtractor.DetermineKeyType(entityModel);
            var keySchema = SchemaGenerator.GenerateKeySchema(keyType);

            return await RegisterKeySchemaAsync(topicName, keySchema);
        }

        private async Task<int> RegisterOrGetValueSchemaIdAsync<T>(EntityModel entityModel)
        {
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
            var valueSchema = SchemaGenerator.GenerateSchema<T>();

            return await RegisterValueSchemaAsync(topicName, valueSchema);
        }

        private async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
        {
            var subject = $"{topicName}-key";
            var schema = new ConfluentSchemaRegistry.Schema(keySchema, ConfluentSchemaRegistry.SchemaType.Avro);
            return await _schemaRegistryClient.RegisterSchemaAsync(subject, schema);
        }

        private async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
        {
            var subject = $"{topicName}-value";
            var schema = new ConfluentSchemaRegistry.Schema(valueSchema, ConfluentSchemaRegistry.SchemaType.Avro);
            return await _schemaRegistryClient.RegisterSchemaAsync(subject, schema);
        }

        private ISerializer<object> CreateKeySerializer<T>(EntityModel entityModel, int schemaId)
        {
            var keyType = KeyExtractor.DetermineKeyType(entityModel);

            return _cache.GetOrCreateSerializer<T>(SerializerType.Key, schemaId, () =>
            {
                if (KeyExtractor.IsCompositeKey(entityModel))
                {
                    return new StringKeySerializer();
                }
                else
                {
                    return new StringKeySerializer();
                }
            });
        }

        private ISerializer<object> CreateValueSerializer<T>(int schemaId)
        {
            return _cache.GetOrCreateSerializer<T>(SerializerType.Value, schemaId, () =>
            {
                return new SimpleAvroSerializer<T>(_schemaRegistryClient);
            });
        }

        private IDeserializer<object> CreateKeyDeserializer<T>(EntityModel entityModel, int schemaId)
        {
            return _cache.GetOrCreateDeserializer<T>(SerializerType.Key, schemaId, () =>
            {
                return new StringKeyDeserializer();
            });
        }

        private IDeserializer<object> CreateValueDeserializer<T>(int schemaId)
        {
            return _cache.GetOrCreateDeserializer<T>(SerializerType.Value, schemaId, () =>
            {
                return new SimpleAvroDeserializer<T>(_schemaRegistryClient);
            });
        }
    }

    internal class StringKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            return System.Text.Encoding.UTF8.GetBytes(data?.ToString() ?? "");
        }
    }

    internal class StringKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return "";
            return System.Text.Encoding.UTF8.GetString(data);
        }
    }

    internal class SimpleAvroSerializer<T> : ISerializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public SimpleAvroSerializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
        {
            _client = client;
        }

        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is T typedData)
            {
                var config = new AvroSerializerConfig { AutoRegisterSchemas = false };
                var serializer = new AvroSerializer<T>(_client, config);
                return serializer.SerializeAsync(typedData, context).GetAwaiter().GetResult();
            }
            throw new InvalidOperationException($"Expected type {typeof(T).Name}");
        }
    }

    internal class SimpleAvroDeserializer<T> : IDeserializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public SimpleAvroDeserializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
        {
            _client = client;
        }

        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            var deserializer = new AvroDeserializer<T>(_client);
            var result = deserializer.DeserializeAsync(data.ToArray(), isNull, context).GetAwaiter().GetResult();
            return result!;
        }
    }
}