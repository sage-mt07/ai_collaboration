using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KsqlDsl.Avro
{
    public class EnhancedAvroSerializerManager
    {
        private readonly ISchemaRegistryClient _schemaRegistryClient;
        private readonly PerformanceMonitoringAvroCache _cache;
        private readonly ResilientAvroSerializerManager _resilientManager;
        private readonly ILogger<EnhancedAvroSerializerManager>? _logger;

        public EnhancedAvroSerializerManager(
            ISchemaRegistryClient schemaRegistryClient,
            PerformanceMonitoringAvroCache cache,
            ResilientAvroSerializerManager resilientManager,
            ILogger<EnhancedAvroSerializerManager>? logger = null)
        {
            _schemaRegistryClient = schemaRegistryClient ?? throw new ArgumentNullException(nameof(schemaRegistryClient));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _resilientManager = resilientManager ?? throw new ArgumentNullException(nameof(resilientManager));
            _logger = logger;
        }

        public async Task<(ISerializer<object>, ISerializer<object>)> CreateSerializersAsync<T>(EntityModel entityModel)
        {
            using var activity = AvroActivitySource.StartCacheOperation("create_serializers", typeof(T).Name);

            var (keySchemaId, valueSchemaId) = await RegisterSchemasWithRetryAsync<T>(entityModel);

            var keySerializer = await CreateKeySerializerAsync<T>(entityModel, keySchemaId);
            var valueSerializer = await CreateValueSerializerAsync<T>(valueSchemaId);

            return (keySerializer, valueSerializer);
        }

        public async Task<(IDeserializer<object>, IDeserializer<object>)> CreateDeserializersAsync<T>(EntityModel entityModel)
        {
            using var activity = AvroActivitySource.StartCacheOperation("create_deserializers", typeof(T).Name);

            var (keySchemaId, valueSchemaId) = await RegisterSchemasWithRetryAsync<T>(entityModel);

            var keyDeserializer = await CreateKeyDeserializerAsync<T>(entityModel, keySchemaId);
            var valueDeserializer = await CreateValueDeserializerAsync<T>(valueSchemaId);

            return (keyDeserializer, valueDeserializer);
        }

        private async Task<(int keySchemaId, int valueSchemaId)> RegisterSchemasWithRetryAsync<T>(EntityModel entityModel)
        {
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
            var (keySchema, valueSchema) = entityModel.GenerateKeyValueSchemas();

            var keySchemaId = await _resilientManager.RegisterSchemaWithRetryAsync($"{topicName}-key", keySchema);
            var valueSchemaId = await _resilientManager.RegisterSchemaWithRetryAsync($"{topicName}-value", valueSchema);

            _cache.RegisterSchema(new AvroSchemaInfo
            {
                EntityType = typeof(T),
                Type = SerializerType.Key,
                SchemaId = keySchemaId,
                Subject = $"{topicName}-key",
                RegisteredAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow,
                SchemaJson = keySchema,
                Version = 1
            });

            _cache.RegisterSchema(new AvroSchemaInfo
            {
                EntityType = typeof(T),
                Type = SerializerType.Value,
                SchemaId = valueSchemaId,
                Subject = $"{topicName}-value",
                RegisteredAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow,
                SchemaJson = valueSchema,
                Version = 1
            });

            return (keySchemaId, valueSchemaId);
        }

        private async Task<ISerializer<object>> CreateKeySerializerAsync<T>(EntityModel entityModel, int schemaId)
        {
            return _cache.GetOrCreateSerializer<T>(SerializerType.Key, schemaId, () =>
            {
                if (KeyExtractor.IsCompositeKey(entityModel))
                {
                    return CreateCompositeKeySerializer(schemaId);
                }
                else
                {
                    var keyType = KeyExtractor.DetermineKeyType(entityModel);
                    return CreatePrimitiveKeySerializer(keyType, schemaId);
                }
            });
        }

        private async Task<ISerializer<object>> CreateValueSerializerAsync<T>(int schemaId)
        {
            return _cache.GetOrCreateSerializer<T>(SerializerType.Value, schemaId, () =>
            {
                var config = new AvroSerializerConfig { AutoRegisterSchemas = false };
                var avroSerializer = new AvroSerializer<T>(_schemaRegistryClient, config);
                return new SerializerWrapper<T>(avroSerializer);
            });
        }

        private async Task<IDeserializer<object>> CreateKeyDeserializerAsync<T>(EntityModel entityModel, int schemaId)
        {
            return _cache.GetOrCreateDeserializer<T>(SerializerType.Key, schemaId, () =>
            {
                if (KeyExtractor.IsCompositeKey(entityModel))
                {
                    return CreateCompositeKeyDeserializer(schemaId);
                }
                else
                {
                    var keyType = KeyExtractor.DetermineKeyType(entityModel);
                    return CreatePrimitiveKeyDeserializer(keyType, schemaId);
                }
            });
        }

        private async Task<IDeserializer<object>> CreateValueDeserializerAsync<T>(int schemaId)
        {
            return _cache.GetOrCreateDeserializer<T>(SerializerType.Value, schemaId, () =>
            {
                var avroDeserializer = new AvroDeserializer<T>(_schemaRegistryClient);
                return new DeserializerWrapper<T>(avroDeserializer);
            });
        }

        private ISerializer<object> CreatePrimitiveKeySerializer(Type keyType, int schemaId)
        {
            if (keyType == typeof(string))
                return new AvroStringKeySerializer();
            if (keyType == typeof(int))
                return new AvroIntKeySerializer();
            if (keyType == typeof(long))
                return new AvroLongKeySerializer();
            if (keyType == typeof(Guid))
                return new AvroGuidKeySerializer();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private IDeserializer<object> CreatePrimitiveKeyDeserializer(Type keyType, int schemaId)
        {
            if (keyType == typeof(string))
                return new AvroStringKeyDeserializer();
            if (keyType == typeof(int))
                return new AvroIntKeyDeserializer();
            if (keyType == typeof(long))
                return new AvroLongKeyDeserializer();
            if (keyType == typeof(Guid))
                return new AvroGuidKeyDeserializer();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private ISerializer<object> CreateCompositeKeySerializer(int schemaId)
        {
            var config = new AvroSerializerConfig { AutoRegisterSchemas = false };
            var avroSerializer = new AvroSerializer<Dictionary<string, object>>(_schemaRegistryClient, config);
            return new SerializerWrapper<Dictionary<string, object>>(avroSerializer);
        }

        private IDeserializer<object> CreateCompositeKeyDeserializer(int schemaId)
        {
            var avroDeserializer = new AvroDeserializer<Dictionary<string, object>>(_schemaRegistryClient);
            return new DeserializerWrapper<Dictionary<string, object>>(avroDeserializer);
        }
    }

    internal class AvroStringKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is string value)
                return System.Text.Encoding.UTF8.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as string key");
        }
    }

    internal class AvroStringKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return null!;
            return System.Text.Encoding.UTF8.GetString(data);
        }
    }

    internal class AvroIntKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is int value)
                return BitConverter.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as int key");
        }
    }

    internal class AvroIntKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return 0;
            return BitConverter.ToInt32(data);
        }
    }

    internal class AvroLongKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is long value)
                return BitConverter.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as long key");
        }
    }

    internal class AvroLongKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return 0L;
            return BitConverter.ToInt64(data);
        }
    }

    internal class AvroGuidKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is Guid value)
                return value.ToByteArray();
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as Guid key");
        }
    }

    internal class AvroGuidKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return Guid.Empty;
            return new Guid(data);
        }
    }
}