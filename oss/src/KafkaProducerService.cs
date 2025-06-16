using Confluent.Kafka;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl;

internal class KafkaProducerService : IDisposable
{
    private readonly KafkaContextOptions _options;
    private readonly KsqlDsl.SchemaRegistry.ISchemaRegistryClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IProducer<object, object>> _producers = new();
    private bool _disposed = false;

    public KafkaProducerService(KafkaContextOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // Schema Registry設定
        if (!string.IsNullOrEmpty(_options.SchemaRegistryUrl))
        {
            var schemaConfig = new KsqlDsl.SchemaRegistry.SchemaRegistryConfig
            {
                Url = _options.SchemaRegistryUrl
            };
            _schemaRegistryClient = new KsqlDsl.SchemaRegistry.Implementation.ConfluentSchemaRegistryClient(schemaConfig);
        }
    }

    public async Task SendAsync<T>(T entity, EntityModel entityModel, CancellationToken cancellationToken = default)
    {
        if (entity == null)
            throw new ArgumentNullException(nameof(entity));
        if (entityModel == null)
            throw new ArgumentNullException(nameof(entityModel));

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
        var producer = GetOrCreateProducer<T>(entityModel);

        try
        {
            var message = CreateMessage(entity, entityModel);
            var deliveryResult = await producer.ProduceAsync(topicName, message, cancellationToken);

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Message sent to {topicName}: Partition={deliveryResult.Partition}, Offset={deliveryResult.Offset}");
            }
        }
        catch (ProduceException<object, object> ex)
        {
            throw new KafkaProducerException(
                $"Failed to send message to topic '{topicName}': {ex.Error.Reason}", ex);
        }
        catch (Exception ex)
        {
            throw new KafkaProducerException(
                $"Unexpected error sending message to topic '{topicName}': {ex.Message}", ex);
        }
    }

    public async Task SendRangeAsync<T>(IEnumerable<T> entities, EntityModel entityModel, CancellationToken cancellationToken = default)
    {
        if (entities == null)
            throw new ArgumentNullException(nameof(entities));
        if (entityModel == null)
            throw new ArgumentNullException(nameof(entityModel));

        var entityList = entities.ToList();
        if (entityList.Count == 0)
            return;

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
        var producer = GetOrCreateProducer<T>(entityModel);

        var tasks = new List<Task>();

        try
        {
            foreach (var entity in entityList)
            {
                var message = CreateMessage(entity, entityModel);
                var task = producer.ProduceAsync(topicName, message, cancellationToken);
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Batch of {entityList.Count} messages sent to {topicName}");
            }
        }
        catch (ProduceException<object, object> ex)
        {
            throw new KafkaProducerException(
                $"Failed to send batch messages to topic '{topicName}': {ex.Error.Reason}", ex);
        }
        catch (Exception ex)
        {
            throw new KafkaProducerException(
                $"Unexpected error sending batch messages to topic '{topicName}': {ex.Message}", ex);
        }
    }

    private IProducer<object, object> GetOrCreateProducer<T>(EntityModel entityModel)
    {
        var entityType = typeof(T);

        if (_producers.TryGetValue(entityType, out var existingProducer))
        {
            return existingProducer;
        }

        var config = BuildProducerConfig();
        var builder = new ProducerBuilder<object, object>(config);

        if (_schemaRegistryClient != null)
        {
            try
            {
                var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;

                var confluentSchemaRegistryConfig = new Confluent.SchemaRegistry.SchemaRegistryConfig
                {
                    Url = _options.SchemaRegistryUrl ?? "http://localhost:8081"
                };

                var confluentClient = new Confluent.SchemaRegistry.CachedSchemaRegistryClient(confluentSchemaRegistryConfig);

                // 修正：Key型を動的に決定
                var keyType = DetermineKeyType(entityModel);

                // 修正：パラメータ型を明示してメソッドを取得
                var keySerializerType = typeof(Confluent.SchemaRegistry.Serdes.AvroSerializer<>).MakeGenericType(keyType);
                var keySerializerInstance = Activator.CreateInstance(keySerializerType, confluentClient, null);

                var valueSerializerType = typeof(Confluent.SchemaRegistry.Serdes.AvroSerializer<>).MakeGenericType(typeof(T));
                var valueSerializerInstance = Activator.CreateInstance(valueSerializerType, confluentClient, null);

                // 修正：パラメータ型を明示してメソッドを取得
                var builderType = builder.GetType();
                var keySerializerInterfaceType = typeof(ISerializer<>).MakeGenericType(keyType);
                var valueSerializerInterfaceType = typeof(ISerializer<>).MakeGenericType(typeof(T));

                var setKeyMethod = builderType.GetMethod("SetKeySerializer", new[] { keySerializerInterfaceType });
                var setValueMethod = builderType.GetMethod("SetValueSerializer", new[] { valueSerializerInterfaceType });

                setKeyMethod.Invoke(builder, new[] { keySerializerInstance });
                setValueMethod.Invoke(builder, new[] { valueSerializerInstance });

                if (_options.EnableDebugLogging)
                {
                    Console.WriteLine($"[DEBUG] Confluent Avroシリアライザーを設定: Key={keyType.Name}, Value={entityType.Name} → Topic: {topicName}");
                }
            }
            catch (Exception ex)
            {
                if (_options.EnableDebugLogging)
                {
                    Console.WriteLine($"[ERROR] Confluent Avroシリアライザー設定エラー: {ex.Message}");
                }

                throw new KafkaProducerException(
                    $"Failed to configure Confluent Avro serializers for {entityType.Name}. " +
                    $"KsqlDsl requires Avro serialization. Ensure Schema Registry is available and accessible: {ex.Message}", ex);
            }
        }
        else
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[ERROR] Schema Registry未設定: KsqlDslはAvro専用設計のため、Schema Registryが必須です");
            }

            throw new KafkaProducerException(
                $"Schema Registry configuration is required for {entityType.Name}. " +
                $"KsqlDsl is designed exclusively for Avro serialization. " +
                $"Please configure Schema Registry URL in KafkaContextOptions.");
        }

        if (_options.EnableDebugLogging)
        {
            var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
            Console.WriteLine($"[DEBUG] Creating producer for {entityType.Name} → Topic: {topicName}");
        }

        var producer = builder.Build();
        _producers[entityType] = producer;

        return producer;
    }

    // Key型決定メソッド
    private Type DetermineKeyType(EntityModel entityModel)
    {
        if (entityModel.KeyProperties.Length == 0)
        {
            return typeof(string); // デフォルトキー
        }

        if (entityModel.KeyProperties.Length == 1)
        {
            return entityModel.KeyProperties[0].PropertyType;
        }

        // 複合キーの場合はDictionaryとして扱う
        return typeof(Dictionary<string, object>);
    }



    private ProducerConfig BuildProducerConfig()
    {
        var config = new ProducerConfig
        {
            BootstrapServers = _options.ConnectionString,
            Acks = Acks.All,
            EnableIdempotence = true,
            MaxInFlight = 1,
            CompressionType = CompressionType.Snappy
        };

        config.Set("retries", "3");
        config.Set("retry.backoff.ms", "100");

        foreach (var kvp in _options.ProducerConfig)
        {
            config.Set(kvp.Key, kvp.Value?.ToString() ?? "");
        }

        return config;
    }

    private Message<object, object> CreateMessage<T>(T entity, EntityModel entityModel)
    {
        var keyValue = ExtractKeyValue(entity, entityModel);

        // 修正：Key値の型変換を削除し、元の型のまま使用
        return new Message<object, object>
        {
            Key = keyValue ?? "", // 型変換なし、nullの場合のみ空文字
            Value = entity        // ValueはAvroでシリアライズ
        };
    }

    private object? ExtractKeyValue<T>(T entity, EntityModel entityModel)
    {
        if (entityModel.KeyProperties.Length == 0)
        {
            return null;
        }

        if (entityModel.KeyProperties.Length == 1)
        {
            var keyProperty = entityModel.KeyProperties[0];
            return keyProperty.GetValue(entity);
        }

        // 複合キー対応
        var keyObject = new Dictionary<string, object?>();
        foreach (var keyProperty in entityModel.KeyProperties)
        {
            var value = keyProperty.GetValue(entity);
            keyObject[keyProperty.Name] = value;
        }

        return keyObject;
    }

    /// <summary>
    /// テスト環境用Avroフォールバックシリアライザー
    /// Schema Registry未設定時にJSONシリアライズで代替
    /// </summary>
    private class TestAvroFallbackSerializer<T> : ISerializer<object>
    {
        public byte[] Serialize(object data, SerializationContext context)
        {
            if (data == null)
                return Array.Empty<byte>();

            try
            {
                // テスト環境ではJSONでシリアライズ（Avroの代替）
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                return System.Text.Encoding.UTF8.GetBytes(json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Test Avro fallback serialization failed for {data.GetType().Name}: {ex.Message}", ex);
            }
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
            foreach (var producer in _producers.Values)
            {
                try
                {
                    producer.Flush(TimeSpan.FromSeconds(10));
                    producer.Dispose();
                }
                catch (Exception ex)
                {
                    if (_options.EnableDebugLogging)
                    {
                        Console.WriteLine($"[DEBUG] Error disposing producer: {ex.Message}");
                    }
                }
            }

            _producers.Clear();
            _schemaRegistryClient?.Dispose();
            _disposed = true;
        }
    }
}

public class KafkaProducerException : Exception
{
    public KafkaProducerException(string message) : base(message) { }
    public KafkaProducerException(string message, Exception innerException) : base(message, innerException) { }
}