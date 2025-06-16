using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KsqlDsl.Avro;
using KsqlDsl.Modeling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KsqlDsl.Communication;

/// <summary>
/// Kafka通信の統合ファサード
/// 設計理由：アプリケーション層に単一のエントリポイントを提供
/// 既存Avro実装（95%完成）との統合により高性能・型安全な通信を実現
/// </summary>
public class KafkaMessageBus : IKafkaMessageBus, IDisposable
{
    private readonly KafkaProducerManager _producerManager;
    private readonly KafkaConsumerManager _consumerManager;
    private readonly PerformanceMonitoringAvroCache _avroCache;
    private readonly ILogger<KafkaMessageBus> _logger;
    private readonly KafkaMessageBusOptions _options;
    private bool _disposed = false;

    public KafkaMessageBus(
        KafkaProducerManager producerManager,
        KafkaConsumerManager consumerManager,
        PerformanceMonitoringAvroCache avroCache,
        IOptions<KafkaMessageBusOptions> options,
        ILogger<KafkaMessageBus> logger)
    {
        _producerManager = producerManager ?? throw new ArgumentNullException(nameof(producerManager));
        _consumerManager = consumerManager ?? throw new ArgumentNullException(nameof(consumerManager));
        _avroCache = avroCache ?? throw new ArgumentNullException(nameof(avroCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("KafkaMessageBus initialized with {ProducerCount} producers, {ConsumerCount} consumers",
            _producerManager.GetActiveProducerCount(), _consumerManager.GetActiveConsumerCount());
    }

    /// <summary>
    /// 単一メッセージ送信
    /// 設計理由：最も頻繁に使用される基本API、型安全性とパフォーマンスを両立
    /// </summary>
    public async Task SendAsync<T>(T message, KafkaMessageContext? context = null, CancellationToken cancellationToken = default) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        using var activity = StartSendActivity<T>("send_single", context);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var producer = await _producerManager.GetProducerAsync<T>();
            try
            {
                var result = await producer.SendAsync(message, context, cancellationToken);
                stopwatch.Stop();

                // メトリクス記録（既存AvroMetrics統合）
                KafkaMetrics.RecordMessageSent(
                    result.Topic,
                    typeof(T).Name,
                    success: true,
                    stopwatch.Elapsed);

                activity?.SetTag("kafka.delivery.partition", result.Partition)
                        ?.SetTag("kafka.delivery.offset", result.Offset)
                        ?.SetStatus(ActivityStatusCode.Ok);

                _logger.LogDebug("Message sent successfully: {EntityType} -> {Topic}:{Partition}:{Offset} ({Duration}ms)",
                    typeof(T).Name, result.Topic, result.Partition, result.Offset, stopwatch.ElapsedMilliseconds);
            }
            finally
            {
                _producerManager.ReturnProducer(producer);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            KafkaMetrics.RecordMessageSent(typeof(T).Name, "unknown", success: false, stopwatch.Elapsed);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to send message: {EntityType} ({Duration}ms)",
                typeof(T).Name, stopwatch.ElapsedMilliseconds);

            throw new KafkaMessageBusException($"Failed to send {typeof(T).Name} message", ex);
        }
    }

    /// <summary>
    /// バッチメッセージ送信
    /// 設計理由：高スループット要件への対応、トランザクション境界の明確化
    /// </summary>
    public async Task SendBatchAsync<T>(IEnumerable<T> messages, KafkaMessageContext? context = null, CancellationToken cancellationToken = default) where T : class
    {
        if (messages == null)
            throw new ArgumentNullException(nameof(messages));

        var messageList = messages.ToList();
        if (messageList.Count == 0)
        {
            _logger.LogDebug("Empty message batch provided for {EntityType}, skipping send", typeof(T).Name);
            return;
        }

        using var activity = StartSendActivity<T>("send_batch", context);
        activity?.SetTag("kafka.batch.size", messageList.Count);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var batchResult = await _producerManager.SendBatchOptimizedAsync(messageList, context, cancellationToken);
            stopwatch.Stop();

            // バッチメトリクス記録
            KafkaMetrics.RecordBatchSent(
                batchResult.Topic,
                messageList.Count,
                success: batchResult.AllSuccessful,
                stopwatch.Elapsed);

            activity?.SetTag("kafka.batch.successful", batchResult.SuccessfulCount)
                    ?.SetTag("kafka.batch.failed", batchResult.FailedCount)
                    ?.SetStatus(batchResult.AllSuccessful ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            _logger.LogInformation("Batch sent: {EntityType} - {SuccessCount}/{TotalCount} successful ({Duration}ms)",
                typeof(T).Name, batchResult.SuccessfulCount, messageList.Count, stopwatch.ElapsedMilliseconds);

            if (!batchResult.AllSuccessful)
            {
                throw new KafkaBatchSendException($"Batch send partially failed: {batchResult.FailedCount}/{messageList.Count} messages failed", batchResult);
            }
        }
        catch (Exception ex) when (!(ex is KafkaBatchSendException))
        {
            stopwatch.Stop();
            KafkaMetrics.RecordBatchSent("unknown", messageList.Count, success: false, stopwatch.Elapsed);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to send batch: {EntityType}, {MessageCount} messages ({Duration}ms)",
                typeof(T).Name, messageList.Count, stopwatch.ElapsedMilliseconds);

            throw new KafkaMessageBusException($"Failed to send {typeof(T).Name} batch ({messageList.Count} messages)", ex);
        }
    }

    /// <summary>
    /// リアルタイム消費ストリーム
    /// 設計理由：非同期ストリーム処理の標準パターン、背圧制御対応
    /// </summary>
    public async IAsyncEnumerable<T> ConsumeAsync<T>(KafkaSubscriptionOptions? options = null, CancellationToken cancellationToken = default) where T : class
    {
        using var activity = StartConsumeActivity<T>("consume_stream");

        var consumer = await _consumerManager.CreateConsumerAsync<T>(options ?? new KafkaSubscriptionOptions());

        try
        {
            var messageCount = 0;
            var startTime = DateTime.UtcNow;

            await foreach (var kafkaMessage in consumer.ConsumeAsync(cancellationToken))
            {
                messageCount++;

                // 定期的なメトリクス更新
                if (messageCount % 100 == 0)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    KafkaMetrics.RecordThroughput("consume", GetTopicName<T>(),
                        (long)(messageCount / elapsed.TotalSeconds));
                }

                yield return kafkaMessage.Value;
            }

            activity?.SetTag("kafka.messages.consumed", messageCount)
                    ?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to consume messages: {EntityType}", typeof(T).Name);
            throw new KafkaMessageBusException($"Failed to consume {typeof(T).Name} messages", ex);
        }
        finally
        {
            consumer.Dispose();
        }
    }

    /// <summary>
    /// バッチフェッチ（Pull型取得）
    /// 設計理由：履歴データ処理、オフセット制御が必要なケースへの対応
    /// </summary>
    public async Task<List<T>> FetchAsync<T>(KafkaFetchOptions options, CancellationToken cancellationToken = default) where T : class
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        using var activity = StartConsumeActivity<T>("fetch_batch");
        activity?.SetTag("kafka.fetch.max_messages", options.MaxMessages)
                ?.SetTag("kafka.fetch.timeout_ms", options.Timeout.TotalMilliseconds);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var consumer = await _consumerManager.CreateConsumerAsync<T>(new KafkaSubscriptionOptions
            {
                GroupId = options.ConsumerGroupId,
                AutoCommit = false
            });

            try
            {
                var batch = await consumer.ConsumeBatchAsync(new KafkaBatchOptions
                {
                    MaxBatchSize = options.MaxMessages,
                    MaxWaitTime = options.Timeout
                }, cancellationToken);

                stopwatch.Stop();

                var messages = batch.Messages.Select(m => m.Value).ToList();

                KafkaMetrics.RecordMessageReceived(GetTopicName<T>(), typeof(T).Name, stopwatch.Elapsed);

                activity?.SetTag("kafka.fetch.actual_messages", messages.Count)
                        ?.SetStatus(ActivityStatusCode.Ok);

                _logger.LogDebug("Fetched {MessageCount} messages of type {EntityType} ({Duration}ms)",
                    messages.Count, typeof(T).Name, stopwatch.ElapsedMilliseconds);

                return messages;
            }
            finally
            {
                consumer.Dispose();
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            _logger.LogError(ex, "Failed to fetch messages: {EntityType} ({Duration}ms)",
                typeof(T).Name, stopwatch.ElapsedMilliseconds);

            throw new KafkaMessageBusException($"Failed to fetch {typeof(T).Name} messages", ex);
        }
    }

    /// <summary>
    /// ヘルスレポート取得
    /// 設計理由：運用監視、障害検出のための統合ビュー提供
    /// </summary>
    public async Task<KafkaHealthReport> GetHealthReportAsync()
    {
        var report = new KafkaHealthReport
        {
            GeneratedAt = DateTime.UtcNow
        };

        try
        {
            // Producer健全性チェック
            report.ProducerHealth = await _producerManager.GetHealthStatusAsync();

            // Consumer健全性チェック
            report.ConsumerHealth = await _consumerManager.GetHealthStatusAsync();

            // 既存Avroキャッシュ健全性取得
            report.AvroHealth = _avroCache.GetHealthReport();

            // 統合パフォーマンス統計
            report.PerformanceStats = new KafkaPerformanceStats
            {
                ProducerStats = _producerManager.GetPerformanceStats(),
                ConsumerStats = _consumerManager.GetPerformanceStats(),
                AvroStats = _avroCache.GetExtendedStatistics()
            };

            // ヘルスレベル決定
            report.HealthLevel = DetermineOverallHealth(report);

            _logger.LogDebug("Health report generated: {HealthLevel}", report.HealthLevel);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate health report");

            return new KafkaHealthReport
            {
                GeneratedAt = DateTime.UtcNow,
                HealthLevel = KafkaHealthLevel.Critical,
                Issues = new List<KafkaHealthIssue>
                {
                    new() { Type = KafkaHealthIssueType.HealthCheckFailure, Description = $"Health check failed: {ex.Message}" }
                }
            };
        }
    }

    /// <summary>
    /// 診断情報取得
    /// 設計理由：トラブルシューティング支援、設定検証
    /// </summary>
    public KafkaDiagnostics GetDiagnostics()
    {
        try
        {
            return new KafkaDiagnostics
            {
                GeneratedAt = DateTime.UtcNow,
                Configuration = _options,
                ProducerDiagnostics = _producerManager.GetDiagnostics(),
                ConsumerDiagnostics = _consumerManager.GetDiagnostics(),
                AvroCache = _avroCache.GetExtendedStatistics(),
                SystemInfo = new Dictionary<string, object>
                {
                    ["Environment"] = Environment.MachineName,
                    ["ProcessId"] = Environment.ProcessId,
                    ["ThreadCount"] = System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
                    ["WorkingSet"] = GC.GetTotalMemory(false)
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate diagnostics");
            throw new KafkaMessageBusException("Failed to generate diagnostics", ex);
        }
    }

    // プライベートヘルパーメソッド

    private Activity? StartSendActivity<T>(string operationName, KafkaMessageContext? context)
    {
        var activity = KafkaActivitySource.StartActivity($"kafka.{operationName}")
            ?.SetTag("kafka.entity.type", typeof(T).Name)
            ?.SetTag("kafka.topic", GetTopicName<T>())
            ?.SetTag("messaging.system", "kafka")
            ?.SetTag("messaging.operation", operationName);

        if (context?.ActivityContext.HasValue == true)
        {
            activity?.SetParentId(context.ActivityContext.Value.TraceId, context.ActivityContext.Value.SpanId);
        }

        if (context?.CorrelationId != null)
        {
            activity?.SetTag("kafka.correlation_id", context.CorrelationId);
        }

        return activity;
    }

    private Activity? StartConsumeActivity<T>(string operationName)
    {
        return KafkaActivitySource.StartActivity($"kafka.{operationName}")
            ?.SetTag("kafka.entity.type", typeof(T).Name)
            ?.SetTag("kafka.topic", GetTopicName<T>())
            ?.SetTag("messaging.system", "kafka")
            ?.SetTag("messaging.operation", operationName);
    }

    private string GetTopicName<T>() where T : class
    {
        // EntityModelから取得（既存実装活用）
        return typeof(T).Name; // 簡略実装、実際はEntityModel.TopicAttribute.TopicNameを使用
    }

    private KafkaHealthLevel DetermineOverallHealth(KafkaHealthReport report)
    {
        if (report.ProducerHealth.HealthLevel == ProducerHealthLevel.Critical ||
            report.ConsumerHealth.HealthLevel == ConsumerHealthLevel.Critical ||
            report.AvroHealth.HealthLevel == CacheHealthLevel.Critical)
        {
            return KafkaHealthLevel.Critical;
        }

        if (report.ProducerHealth.HealthLevel == ProducerHealthLevel.Warning ||
            report.ConsumerHealth.HealthLevel == ConsumerHealthLevel.Warning ||
            report.AvroHealth.HealthLevel == CacheHealthLevel.Warning)
        {
            return KafkaHealthLevel.Warning;
        }

        return KafkaHealthLevel.Healthy;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogInformation("Disposing KafkaMessageBus...");

            _producerManager?.Dispose();
            _consumerManager?.Dispose();
            // _avroCache は外部管理のためDisposeしない

            _disposed = true;
            _logger.LogInformation("KafkaMessageBus disposed successfully");
        }
    }
}