using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.SchemaRegistry;
using Microsoft.Extensions.Logging;

namespace KsqlDsl.Avro
{
    public class AvroSerializerManager
    {
        private readonly ISchemaRegistryClient _schemaRegistryClient;
        private readonly AvroSerializerCache _cache;
        private readonly ILogger<AvroSerializerManager>? _logger;

        public AvroSerializerManager(
            ISchemaRegistryClient schemaRegistryClient,
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

            return await _schemaRegistryClient.RegisterKeySchemaAsync(topicName, keySchema);
        }

        private async Task<int> RegisterOrGetValueSchemaIdAsync<T>(EntityModel entityModel)
        {
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
            var valueSchema = SchemaGenerator.GenerateSchema<T>();

            return await _schemaRegistryClient.RegisterValueSchemaAsync(topicName, valueSchema);
        }

        private ISerializer<object> CreateKeySerializer<T>(EntityModel entityModel, int schemaId)
        {
            var keyType = KeyExtractor.DetermineKeyType(entityModel);

            return _cache.GetOrCreateSerializer<T>(SerializerType.Key, schemaId, () =>
            {
                if (KeyExtractor.IsCompositeKey(entityModel))
                {
                    // 複合キーの場合はDictionaryシリアライザーを使用
                    return CreateDictionarySerializer(schemaId);
                }
                else
                {
                    // 単一キーの場合
                    return CreatePrimitiveSerializer(keyType, schemaId);
                }
            });
        }

        private ISerializer<object> CreateValueSerializer<T>(int schemaId)
        {
            return _cache.GetOrCreateSerializer<T>(SerializerType.Value, schemaId, () =>
            {
                // Confluent Avro Serializerを使用
                var config = new AvroSerializerConfig
                {
                    AutoRegisterSchemas = false // スキーマは事前登録済み
                };

                var avroSerializer = new AvroSerializer<T>(_schemaRegistryClient, config);
                return new SerializerWrapper<T>(avroSerializer);
            });
        }

        private IDeserializer<object> CreateKeyDeserializer<T>(EntityModel entityModel, int schemaId)
        {
            var keyType = KeyExtractor.DetermineKeyType(entityModel);

            return _cache.GetOrCreateDeserializer<T>(SerializerType.Key, schemaId, () =>
            {
                if (KeyExtractor.IsCompositeKey(entityModel))
                {
                    return CreateDictionaryDeserializer(schemaId);
                }
                else
                {
                    return CreatePrimitiveDeserializer(keyType, schemaId);
                }
            });
        }

        private IDeserializer<object> CreateValueDeserializer<T>(int schemaId)
        {
            return _cache.GetOrCreateDeserializer<T>(SerializerType.Value, schemaId, () =>
            {
                var avroDeserializer = new AvroDeserializer<T>(_schemaRegistryClient);
                return new DeserializerWrapper<T>(avroDeserializer);
            });
        }

        private ISerializer<object> CreatePrimitiveSerializer(Type keyType, int schemaId)
        {
            // 型に応じてプリミティブシリアライザーを作成
            if (keyType == typeof(string))
                return new PrimitiveSerializer<string>();
            if (keyType == typeof(int))
                return new PrimitiveSerializer<int>();
            if (keyType == typeof(long))
                return new PrimitiveSerializer<long>();
            if (keyType == typeof(Guid))
                return new PrimitiveSerializer<Guid>();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private IDeserializer<object> CreatePrimitiveDeserializer(Type keyType, int schemaId)
        {
            if (keyType == typeof(string))
                return new PrimitiveDeserializer<string>();
            if (keyType == typeof(int))
                return new PrimitiveDeserializer<int>();
            if (keyType == typeof(long))
                return new PrimitiveDeserializer<long>();
            if (keyType == typeof(Guid))
                return new PrimitiveDeserializer<Guid>();

            throw new NotSupportedException($"Key type {keyType.Name} is not supported");
        }

        private ISerializer<object> CreateDictionarySerializer(int schemaId)
        {
            var config = new AvroSerializerConfig { AutoRegisterSchemas = false };
            var avroSerializer = new AvroSerializer<Dictionary<string, object>>(_schemaRegistryClient, config);
            return new SerializerWrapper<Dictionary<string, object>>(avroSerializer);
        }

        private IDeserializer<object> CreateDictionaryDeserializer(int schemaId)
        {
            var avroDeserializer = new AvroDeserializer<Dictionary<string, object>>(_schemaRegistryClient);
            return new DeserializerWrapper<Dictionary<string, object>>(avroDeserializer);
        }
    }

    // ラッパークラス - 型安全性を保つ
    internal class SerializerWrapper<T> : ISerializer<object>
    {
        private readonly ISerializer<T> _inner;

        public SerializerWrapper(ISerializer<T> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is T typedData)
                return _inner.Serialize(typedData, context);

            throw new InvalidOperationException($"Expected type {typeof(T).Name}, got {data?.GetType().Name ?? "null"}");
        }
    }

    internal class DeserializerWrapper<T> : IDeserializer<object>
    {
        private readonly IDeserializer<T> _inner;

        public DeserializerWrapper(IDeserializer<T> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            var result = _inner.Deserialize(data, isNull, context);
            return result!;
        }
    }

    // プリミティブ型用シリアライザー
    internal class PrimitiveSerializer<T> : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data is T value)
            {
                if (typeof(T) == typeof(string))
                    return System.Text.Encoding.UTF8.GetBytes(value.ToString()!);
                if (typeof(T) == typeof(int))
                    return BitConverter.GetBytes((int)(object)value);
                if (typeof(T) == typeof(long))
                    return BitConverter.GetBytes((long)(object)value);
                if (typeof(T) == typeof(Guid))
                    return ((Guid)(object)value).ToByteArray();
            }

            throw new InvalidOperationException($"Cannot serialize {data?.GetType().Name} as {typeof(T).Name}");
        }
    }

    internal class PrimitiveDeserializer<T> : IDeserializer<object>
    {
        public object Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        {
            if (isNull) return default(T)!;

            if (typeof(T) == typeof(string))
                return System.Text.Encoding.UTF8.GetString(data);
            if (typeof(T) == typeof(int))
                return BitConverter.ToInt32(data);
            if (typeof(T) == typeof(long))
                return BitConverter.ToInt64(data);
            if (typeof(T) == typeof(Guid))
                return new Guid(data);

            throw new InvalidOperationException($"Cannot deserialize to {typeof(T).Name}");
        }
    }