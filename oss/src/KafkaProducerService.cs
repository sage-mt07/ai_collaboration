using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.SchemaRegistry;
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
    private readonly ISchemaRegistryClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IProducer<object, object>> _producers = new();
    private bool _disposed = false;

    public KafkaProducerService(KafkaContextOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (!string.IsNullOrEmpty(_options.SchemaRegistryUrl))
        {
            var schemaConfig = new SchemaRegistryConfig
            {
                Url = _options.SchemaRegistryUrl
            };
            _schemaRegistryClient = new CachedSchemaRegistryClient(schemaConfig);
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

        // Avro serializers setup
        if (_schemaRegistryClient != null && _options.EnableAutoSchemaRegistration)
        {
            builder.SetKeySerializer(new AvroSerializer<object>(_schemaRegistryClient));
            builder.SetValueSerializer(new AvroSerializer<object>(_schemaRegistryClient));
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
            Retries = 3,
            EnableIdempotence = true,
            MaxInFlight = 1,
            CompressionType = CompressionType.Snappy
        };

        // Add custom producer configuration
        foreach (var kvp in _options.ProducerConfig)
        {
            config.Set(kvp.Key, kvp.Value);
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

        // Composite key - create anonymous object
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