using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.KsqlDb;
// 修正理由：CS0104エラー回避のためエイリアス使用
using ConfluentSchemaClient = Confluent.SchemaRegistry.ISchemaRegistryClient;
using KsqlSchemaClient = KsqlDsl.SchemaRegistry.ISchemaRegistryClient;
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
    // 修正理由：CS0019エラー回避のためKsqlSchemaClientエイリアス使用
    private readonly KsqlSchemaClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IConsumer<object, object>> _consumers = new();
    // 修正理由：Phase3-1でksqlDB REST API統合
    private readonly Lazy<KsqlDbRestApiClient> _ksqlDbClient;
    private bool _disposed = false;

    public KafkaConsumerService(KafkaContextOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        // 修正理由：ProducerServiceと同様のSchemaRegistry設定
        if (!string.IsNullOrEmpty(_options.SchemaRegistryUrl))
        {
            var schemaConfig = new KsqlDsl.SchemaRegistry.SchemaRegistryConfig
            {
                Url = _options.SchemaRegistryUrl
            };
            _schemaRegistryClient = new KsqlDsl.SchemaRegistry.Implementation.ConfluentSchemaRegistryClient(schemaConfig);
        }

        // 修正理由：Phase3-1でksqlDB REST API統合（ConnectionStringをksqlDB URLとして使用）
        _ksqlDbClient = new Lazy<KsqlDbRestApiClient>(() =>
        {
            // ksqlDB URLが明示的に設定されていない場合、Kafka接続文字列から推測
            var ksqlDbUrl = DeriveKsqlDbUrl(_options.ConnectionString);
            return new KsqlDbRestApiClient(ksqlDbUrl);
        });
    }

    /// <summary>
    /// KSQL クエリに基づいてデータを取得（Phase 3-1：Pull Query実装）
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
            // 修正理由：Phase3-1でksqlDB Pull Query実行に変更
            var ksqlResponse = await _ksqlDbClient.Value.ExecuteQueryAsync(ksqlQuery, cancellationToken);

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] ksqlDB Response: {ksqlResponse.Rows.Count} rows received");
            }

            // 修正理由：task_eventset.txt「デシリアライズ」に準拠したデータ変換実装
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
                    // 修正理由：設計ドキュメント「例外設計厳守」- デシリアライズエラーは警告レベル
                    // 個別行エラーは全体を止めず、ログ出力のみ
                }
            }

            return results;
        }
        catch (KsqlDbException ex)
        {
            // 修正理由：task_eventset.txt「例外設計厳守」に準拠
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
    /// ksqlDBレスポンス行をPOCOエンティティにデシリアライズ
    /// </summary>
    private T? DeserializeRowToEntity<T>(Dictionary<string, object> row, EntityModel entityModel)
    {
        try
        {
            // 修正理由：Phase3-1基本実装、System.Text.Jsonを使用したシンプルなデシリアライズ
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
    /// Kafka接続文字列からksqlDB URLを推測
    /// </summary>
    private string DeriveKsqlDbUrl(string? kafkaConnectionString)
    {
        if (string.IsNullOrEmpty(kafkaConnectionString))
        {
            return "http://localhost:8088"; // デフォルトksqlDB URL
        }

        // 修正理由：task_eventset.txt設計に従い、localhost:9092 → localhost:8088変換
        if (kafkaConnectionString.Contains("localhost:9092"))
        {
            return "http://localhost:8088";
        }

        // より複雑な推測ロジックは後で実装
        return "http://localhost:8088";
    }

    /// <summary>
    /// 従来のKafka Consumer購読機能（Push型ストリーム用）
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

        // Phase 3-1: Avroデシリアライザーは後で実装
        // 修正理由：Phase 3-2でAvro統合予定のため、一時的にスタブ
        if (_schemaRegistryClient != null && _options.EnableAutoSchemaRegistration)
        {
            // TODO: Phase 3-2でAvroDeserializer実装
            // builder.SetKeyDeserializer(new AvroDeserializer<object>(_schemaRegistryClient));
            // builder.SetValueDeserializer(new AvroDeserializer<object>(_schemaRegistryClient));
        }

        var consumer = builder.Build();
        _consumers[entityType] = consumer;

        return consumer;
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

        // 修正理由：ProducerServiceと同様の設定適用方法
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

            // 修正理由：Phase3-1でksqlDB REST APIクライアント追加
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