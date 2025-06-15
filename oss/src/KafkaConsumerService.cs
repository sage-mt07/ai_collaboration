using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
// 修正理由：CS0104エラー回避のためエイリアス使用
using ConfluentSchemaClient = Confluent.SchemaRegistry.ISchemaRegistryClient;
using KsqlSchemaClient = KsqlDsl.SchemaRegistry.ISchemaRegistryClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl;

internal class KafkaConsumerService : IDisposable
{
    private readonly KafkaContextOptions _options;
    // 修正理由：CS0019エラー回避のためKsqlSchemaClientエイリアス使用
    private readonly KsqlSchemaClient? _schemaRegistryClient;
    private readonly Dictionary<Type, IConsumer<object, object>> _consumers = new();
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
    }

    /// <summary>
    /// KSQL クエリに基づいてデータを取得（Phase 3-1：基盤実装）
    /// </summary>
    public async Task<List<T>> QueryAsync<T>(string ksqlQuery, EntityModel entityModel, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ksqlQuery))
            throw new ArgumentException("KSQL query cannot be null or empty", nameof(ksqlQuery));
        if (entityModel == null)
            throw new ArgumentNullException(nameof(entityModel));

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
        var consumer = GetOrCreateConsumer<T>(entityModel);

        try
        {
            // Phase 3-1: 基本的なトピック購読実装
            // 修正理由：task_eventset.txt「LINQ→KSQL変換→Kafka/ksqlDBから該当データを取得」に準拠
            consumer.Subscribe(topicName);

            var results = new List<T>();
            var timeout = TimeSpan.FromSeconds(5); // Phase 3-1では固定タイムアウト

            if (_options.EnableDebugLogging)
            {
                Console.WriteLine($"[DEBUG] Consumer.QueryAsync: {typeof(T).Name} ← Topic: {topicName}");
                Console.WriteLine($"[DEBUG] KSQL Query: {ksqlQuery}");
            }

            // 修正理由：CS1998警告回避 + 実際の非同期スケジューリング
            // Task.CompletedTaskよりTask.Yield()が適切（真の非同期処理）
            await Task.Yield();

            // Phase 3-1: 基本的なポーリング実装（後でKSQL統合）
            var startTime = DateTime.UtcNow;
            while (DateTime.UtcNow - startTime < timeout && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var consumeResult = consumer.Consume(TimeSpan.FromMilliseconds(100));
                    if (consumeResult?.Message?.Value != null)
                    {
                        // Phase 3-1: 基本的なデシリアライゼーション（後で改善）
                        if (consumeResult.Message.Value is T typedValue)
                        {
                            results.Add(typedValue);
                        }
                    }
                }
                catch (ConsumeException ex)
                {
                    if (_options.EnableDebugLogging)
                    {
                        Console.WriteLine($"[DEBUG] Consume error: {ex.Error.Reason}");
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            throw new KafkaConsumerException(
                $"Failed to query topic '{topicName}': {ex.Message}", ex);
        }
        finally
        {
            // Phase 3-1: 基本的なクリーンアップ
            try
            {
                consumer.Unsubscribe();
            }
            catch (Exception ex)
            {
                if (_options.EnableDebugLogging)
                {
                    Console.WriteLine($"[DEBUG] Unsubscribe error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 同期版クエリ実行
    /// </summary>
    public List<T> Query<T>(string ksqlQuery, EntityModel entityModel)
    {
        return QueryAsync<T>(ksqlQuery, entityModel).GetAwaiter().GetResult();
    }

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
            _disposed = true;
        }
    }
}

public class KafkaConsumerException : Exception
{
    public KafkaConsumerException(string message) : base(message) { }
    public KafkaConsumerException(string message, Exception innerException) : base(message, innerException) { }
}