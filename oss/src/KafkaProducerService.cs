using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        // 基本機能確認後、Avroシリアライザーを段階的に実装予定
        if (_schemaRegistryClient != null && _options.EnableAutoSchemaRegistration)
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Schema registry available for {entityType.Name}, Avro support deferred");
            }

            // 将来的にスキーマ自動登録を実装
            Task.Run(async () =>
            {
                try
                {
                    await RegisterEntitySchemasAsync<T>(entityModel);
                }
                catch (Exception schemaEx)
                {
                    if (_options.EnableDebugLogging)
                    {
                        Console.WriteLine($"[DEBUG] Schema registration error for {entityType.Name}: {schemaEx.Message}");
                    }
                }
            });
        }

        var producer = builder.Build();
        _producers[entityType] = producer;

        return producer;
    }

    /// <summary>
    /// エンティティ用のスキーマを自動登録（将来実装）
    /// </summary>
    private async Task RegisterEntitySchemasAsync<T>(EntityModel entityModel)
    {
        if (_schemaRegistryClient == null) return;

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
        var entityType = typeof(T);

        try
        {
            // 基本的なスキーマ生成（将来的にAvro対応予定）
            var keySchema = "\"string\""; // デフォルトキー
            var valueSchema = GenerateBasicSchema<T>();

            // Schema Registryに登録
            var (keySchemaId, valueSchemaId) = await _schemaRegistryClient.RegisterTopicSchemasAsync(
                topicName, keySchema, valueSchema);

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Basic schemas registered for {entityType.Name} → Topic: {topicName}");
                Console.WriteLine($"[DEBUG] Key Schema ID: {keySchemaId}, Value Schema ID: {valueSchemaId}");
            }
        }
        catch (Exception ex)
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Schema registration failed for {entityType.Name}: {ex.Message}");
            }
            // スキーマ登録失敗はProducer生成を止めない
        }
    }

    /// <summary>
    /// 基本的なスキーマ生成（簡易版）
    /// </summary>
    private string GenerateBasicSchema<T>()
    {
        var entityType = typeof(T);
        var properties = entityType.GetProperties();

        // 簡易JSON Schema形式
        var schema = new
        {
            type = "record",
            name = entityType.Name,
            @namespace = "KsqlDsl.Generated",
            fields = properties.Select(p => new
            {
                name = p.Name,
                type = MapToBasicType(p.PropertyType)
            }).ToArray()
        };

        return System.Text.Json.JsonSerializer.Serialize(schema);
    }

    private string MapToBasicType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType switch
        {
            Type t when t == typeof(string) => "string",
            Type t when t == typeof(int) => "int",
            Type t when t == typeof(long) => "long",
            Type t when t == typeof(bool) => "boolean",
            Type t when t == typeof(float) => "float",
            Type t when t == typeof(double) => "double",
            Type t when t == typeof(decimal) => "double",
            Type t when t == typeof(DateTime) => "string",
            Type t when t == typeof(Guid) => "string",
            _ => "string"
        };
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

        return new Message<object, object>
        {
            Key = keyValue,
            Value = entity
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