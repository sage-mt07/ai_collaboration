using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.KsqlDb;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace KsqlDsl;

internal class KafkaConsumerService : IDisposable
{
    private readonly KafkaContextOptions _options;
    private readonly KsqlDsl.SchemaRegistry.ISchemaRegistryClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IConsumer<object, object>> _consumers = new();
    private readonly Lazy<KsqlDbRestApiClient> _ksqlDbClient;
    private bool _disposed = false;

    public KafkaConsumerService(KafkaContextOptions options)
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

        // ksqlDB REST API統合
        _ksqlDbClient = new Lazy<KsqlDbRestApiClient>(() =>
        {
            var ksqlDbUrl = DeriveKsqlDbUrl(_options.ConnectionString);
            return new KsqlDbRestApiClient(ksqlDbUrl);
        });
    }

    /// <summary>
    /// KSQL クエリに基づいてデータを取得（Pull Query実装）
    /// </summary>
    public async Task<List<T>> QueryAsync<T>(string ksqlQuery, EntityModel entityModel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ksqlQuery))
            throw new ArgumentException("KSQL query cannot be null or empty", nameof(ksqlQuery));
        if (entityModel == null)
            throw new ArgumentNullException(nameof(entityModel));

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;

        if (_options.EnableDebugLogging)
        {
            Console.WriteLine($"[DEBUG] Consumer.QueryAsync: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] KSQL Query: {ksqlQuery}");
        }

        try
        {
            var ksqlResponse = await _ksqlDbClient.Value.ExecuteQueryAsync(ksqlQuery, cancellationToken);

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] ksqlDB Response: {ksqlResponse.Rows.Count} rows received");
            }

            var results = new List<T>();
            foreach (var row in ksqlResponse.Rows)
            {
                try
                {
                    var entity = DeserializeRowToEntity<T>(row, entityModel);
                    if (entity != null)
                    {
                        results.Add(entity);
                    }
                }
                catch (Exception deserializeEx)
                {
                    if (_options.EnableDebugLogging)
                    {
                        Console.WriteLine($"[DEBUG] Deserialization error for row: {deserializeEx.Message}");
                    }
                }
            }

            return results;
        }
        catch (KsqlDbException ex)
        {
            throw new KafkaConsumerException(
                $"Failed to execute ksqlDB query on topic '{topicName}': {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new KafkaConsumerException(
                $"Unexpected error querying topic '{topicName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 同期版クエリ実行
    /// </summary>
    public List<T> Query<T>(string ksqlQuery, EntityModel entityModel)
    {
        return QueryAsync<T>(ksqlQuery, entityModel).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Push型ストリーミング購読（EventSet Subscribe系用）
    /// </summary>
    public async Task SubscribeStreamAsync<T>(
        string ksqlQuery,
        EntityModel entityModel,
        Func<T, Task> onNext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ksqlQuery))
            throw new ArgumentException("KSQL query cannot be null or empty", nameof(ksqlQuery));
        if (entityModel == null)
            throw new ArgumentNullException(nameof(entityModel));
        if (onNext == null)
            throw new ArgumentNullException(nameof(onNext));

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;

        if (_options.EnableDebugLogging)
        {
            Console.WriteLine($"[DEBUG] Consumer.SubscribeStreamAsync: {typeof(T).Name} ← Topic: {topicName}");
            Console.WriteLine($"[DEBUG] Streaming KSQL Query: {ksqlQuery}");
        }

        try
        {
            await _ksqlDbClient.Value.ExecuteStreamingQueryAsync(
                ksqlQuery,
                async (row) =>
                {
                    try
                    {
                        var entity = DeserializeRowToEntity<T>(row, entityModel);
                        if (entity != null)
                        {
                            await onNext(entity);
                        }
                    }
                    catch (Exception deserializeEx)
                    {
                        if (_options.EnableDebugLogging)
                        {
                            Console.WriteLine($"[DEBUG] Stream deserialization error: {deserializeEx.Message}");
                        }
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Streaming query cancelled for {typeof(T).Name}");
            }
            throw;
        }
        catch (KsqlDbException ex)
        {
            throw new KafkaConsumerException(
                $"Failed to execute ksqlDB streaming query on topic '{topicName}': {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new KafkaConsumerException(
                $"Unexpected error streaming from topic '{topicName}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ksqlDBレスポンス行をPOCOエンティティにデシリアライズ
    /// </summary>
    private T? DeserializeRowToEntity<T>(Dictionary<string, object> row, EntityModel entityModel)
    {
        try
        {
            // JSON デシリアライズを使用（基本実装）
            var jsonString = JsonSerializer.Serialize(row);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var entity = JsonSerializer.Deserialize<T>(jsonString, options);
            return entity;
        }
        catch (JsonException ex)
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] JSON deserialization error: {ex.Message}");
            }
            return default(T);
        }
    }

    /// <summary>
    /// 従来のKafka Consumer購読機能（将来拡張用）
    /// </summary>
    private IConsumer<object, object> GetOrCreateConsumer<T>(EntityModel entityModel)
    {
        var entityType = typeof(T);

        if (_consumers.TryGetValue(entityType, out var existingConsumer))
        {
            return existingConsumer;
        }

        var config = BuildConsumerConfig();
        var builder = new ConsumerBuilder<object, object>(config);

        // 基本機能確認後、Avroデシリアライザーを段階的に実装予定
        if (_schemaRegistryClient != null && _options.EnableAutoSchemaRegistration)
        {
            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Schema registry available for {entityType.Name}, Avro support deferred");
            }
        }

        var consumer = builder.Build();
        _consumers[entityType] = consumer;

        return consumer;
    }

    /// <summary>
    /// Kafka接続文字列からksqlDB URLを推測
    /// </summary>
    private string DeriveKsqlDbUrl(string? kafkaConnectionString)
    {
        if (string.IsNullOrEmpty(kafkaConnectionString))
        {
            return "http://localhost:8088";
        }

        if (kafkaConnectionString.Contains("localhost:9092"))
        {
            return "http://localhost:8088";
        }

        return "http://localhost:8088";
    }

    private ConsumerConfig BuildConsumerConfig()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.ConnectionString,
            GroupId = _options.ConsumerGroupId ?? $"ksqldsl-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true,
            EnablePartitionEof = false
        };

        config.Set("fetch.wait.max.ms", "500");
        config.Set("fetch.min.bytes", "1");

        foreach (var kvp in _options.ConsumerConfig)
        {
            config.Set(kvp.Key, kvp.Value?.ToString() ?? "");
        }

        return config;
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
            foreach (var consumer in _consumers.Values)
            {
                try
                {
                    consumer.Close();
                    consumer.Dispose();
                }
                catch (Exception ex)
                {
                    if (_options.EnableDebugLogging)
                    {
                        Console.WriteLine($"[DEBUG] Error disposing consumer: {ex.Message}");
                    }
                }
            }

            _consumers.Clear();
            _schemaRegistryClient?.Dispose();

            if (_ksqlDbClient.IsValueCreated)
            {
                _ksqlDbClient.Value.Dispose();
            }

            _disposed = true;
        }
    }
}

public class KafkaConsumerException : Exception
{
    public KafkaConsumerException(string message) : base(message) { }
    public KafkaConsumerException(string message, Exception innerException) : base(message, innerException) { }
}