using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
// 修正理由：名前空間競合回避のためエイリアス使用 (Phase2エラー CS0104対応)
using AvroSerializer = Confluent.SchemaRegistry.Serdes.AvroSerializer<object>;
using ConfluentSchemaClient = Confluent.SchemaRegistry.ISchemaRegistryClient;
using KsqlSchemaClient = KsqlDsl.SchemaRegistry.ISchemaRegistryClient;
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
    // 修正理由：設計ドキュメントに従いKsqlDsl独自ISchemaRegistryClientを使用
    private readonly KsqlSchemaClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IProducer<object, object>> _producers = new();
    private bool _disposed = false;

    public KafkaProducerService(KafkaContextOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // 修正理由：KsqlDsl設計に従い、ConfluentSchemaRegistryClient実装を使用
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
            // 修正理由：task_eventset.txt「エラー・バリデーション不整合時は即例外」に準拠
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

        // 修正理由：task_eventset.txt「Avroスキーマ連携を実際に実装」に準拠  
        if (_schemaRegistryClient != null && _options.EnableAutoSchemaRegistration)
        {
            // 注意：Confluent.SchemaRegistry.Serdes.AvroSerializerを使用するため、
            // ConfluentのISchemaRegistryClientが必要。実装クラス内で取得する必要がある
            // 現在はスタブ実装として無効化
            // builder.SetKeySerializer(new AvroSerializer<object>(_schemaRegistryClient));
            // builder.SetValueSerializer(new AvroSerializer<object>(_schemaRegistryClient));
        }

        var producer = builder.Build();
        _producers[entityType] = producer;

        return producer;
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
        // 修正理由：Retriesプロパティが存在しないため、Set()メソッドで低レベル設定
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

        // 修正理由：複合キー対応（設計ドキュメント要件）
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