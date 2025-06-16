using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

namespace KsqlDsl.Avro
{
    public class EnhancedAvroSerializerManager
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _schemaRegistryClient;
        private readonly PerformanceMonitoringAvroCache _cache;
        private readonly ResilientAvroSerializerManager _resilientManager;
        private readonly ILogger<EnhancedAvroSerializerManager>? _logger;

        public EnhancedAvroSerializerManager(
            ConfluentSchemaRegistry.ISchemaRegistryClient schemaRegistryClient,
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

        private Task<ISerializer<object>> CreateKeySerializerAsync<T>(EntityModel entityModel, int schemaId)
        {
            return Task.FromResult(_cache.GetOrCreateSerializer<T>(SerializerType.Key, schemaId, () =>
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
            }));
        }

        private Task<ISerializer<object>> CreateValueSerializerAsync<T>(int schemaId)
        {
            return Task.FromResult(_cache.GetOrCreateSerializer<T>(SerializerType.Value, schemaId, () =>
            {
                return new EnhancedAvroSerializer<T>(_schemaRegistryClient);
            }));
        }

        private Task<IDeserializer<object>> CreateKeyDeserializerAsync<T>(EntityModel entityModel, int schemaId)
        {
            return Task.FromResult(_cache.GetOrCreateDeserializer<T>(SerializerType.Key, schemaId, () =>
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
            }));
        }

        private Task<IDeserializer<object>> CreateValueDeserializerAsync<T>(int schemaId)
        {
            return Task.FromResult(_cache.GetOrCreateDeserializer<T>(SerializerType.Value, schemaId, () =>
            {
                return new EnhancedAvroDeserializer<T>(_schemaRegistryClient);
            }));
        }

        private ISerializer<object> CreatePrimitiveKeySerializer(Type keyType, int schemaId)
        {
            if (keyType == typeof(string))
                return new EnhancedStringKeySerializer();
            if (keyType == typeof(int))
                return new EnhancedIntKeySerializer();
            if (keyType == typeof(long))
                return new EnhancedLongKeySerializer();
            if (keyType == typeof(Guid))
                return new EnhancedGuidKeySerializer();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private IDeserializer<object> CreatePrimitiveKeyDeserializer(Type keyType, int schemaId)
        {
            if (keyType == typeof(string))
                return new EnhancedStringKeyDeserializer();
            if (keyType == typeof(int))
                return new EnhancedIntKeyDeserializer();
            if (keyType == typeof(long))
                return new EnhancedLongKeyDeserializer();
            if (keyType == typeof(Guid))
                return new EnhancedGuidKeyDeserializer();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private ISerializer<object> CreateCompositeKeySerializer(int schemaId)
        {
            return new EnhancedCompositeKeySerializer(_schemaRegistryClient);
        }

        private IDeserializer<object> CreateCompositeKeyDeserializer(int schemaId)
        {
            return new EnhancedCompositeKeyDeserializer(_schemaRegistryClient);
        }
    }

    internal class EnhancedAvroSerializer<T> : ISerializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public EnhancedAvroSerializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
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

    internal class EnhancedAvroDeserializer<T> : IDeserializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public EnhancedAvroDeserializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
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

    internal class EnhancedStringKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is string value)
                return System.Text.Encoding.UTF8.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as string key");
        }
    }

    internal class EnhancedStringKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return string.Empty;
            return System.Text.Encoding.UTF8.GetString(data);
        }
    }

    internal class EnhancedIntKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is int value)
                return BitConverter.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as int key");
        }
    }

    internal class EnhancedIntKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return 0;
            return BitConverter.ToInt32(data);
        }
    }

    internal class EnhancedLongKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is long value)
                return BitConverter.GetBytes(value);
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as long key");
        }
    }

    internal class EnhancedLongKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return 0L;
            return BitConverter.ToInt64(data);
        }
    }

    internal class EnhancedGuidKeySerializer : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is Guid value)
                return value.ToByteArray();
            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as Guid key");
        }
    }

    internal class EnhancedGuidKeyDeserializer : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return Guid.Empty;
            return new Guid(data);
        }
    }

    internal class EnhancedCompositeKeySerializer : ISerializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public EnhancedCompositeKeySerializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
        {
            _client = client;
        }

        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is Dictionary<string, object> dict)
            {
                var config = new AvroSerializerConfig { AutoRegisterSchemas = false };
                var serializer = new AvroSerializer<Dictionary<string, object>>(_client, config);
                return serializer.SerializeAsync(dict, context).GetAwaiter().GetResult();
            }
            throw new InvalidOperationException("Expected Dictionary<string, object> for composite key");
        }
    }

    internal class EnhancedCompositeKeyDeserializer : IDeserializer<object>
    {
        private readonly ConfluentSchemaRegistry.ISchemaRegistryClient _client;

        public EnhancedCompositeKeyDeserializer(ConfluentSchemaRegistry.ISchemaRegistryClient client)
        {
            _client = client;
        }

        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return new Dictionary<string, object>();
            var deserializer = new AvroDeserializer<Dictionary<string, object>>(_client);
            var result = deserializer.DeserializeAsync(data.ToArray(), isNull, context).GetAwaiter().GetResult();
            return result!;
        }
    }
}