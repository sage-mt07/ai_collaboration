// =============================================================================================
// DLQ (Dead Letter Queue) Implementation
// 設計理念：「復元可能な状態での記録」「バイナリ形式での忠実な保持」「拡張可能性」
// =============================================================================================

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.Messaging.Dlq;

// =============================================================================================
// Core Data Structures - コアデータ構造
// =============================================================================================

/// <summary>
/// DLQに保存されるメッセージ構造
/// 設計原則：Kafkaメッセージのバイナリ形式を忠実に保持、完全復元可能性確保
/// </summary>
public class DlqMessage
{
    /// <summary>
    /// 元メッセージのキー（シリアライズ済みバイナリ）
    /// null許容：Kafkaのキーレスメッセージに対応
    /// </summary>
    public byte[]? Key { get; set; }

    /// <summary>
    /// 元メッセージの値（シリアライズ済みバイナリ）
    /// null許容：tombstone メッセージ（削除指示）に対応
    /// </summary>
    public byte[]? Value { get; set; }

    /// <summary>
    /// 失敗情報・再処理情報（文字列形式メタデータ）
    /// Kafka Headers に変換可能な形式
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// DLQ記録日時（UTC）
    /// </summary>
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 元メッセージのトピック名
    /// 再処理時の送信先決定に使用
    /// </summary>
    public string OriginalTopic { get; set; } = string.Empty;

    /// <summary>
    /// 元メッセージのパーティション番号
    /// 再処理時の順序保証に使用
    /// </summary>
    public int? OriginalPartition { get; set; }

    /// <summary>
    /// 元メッセージのオフセット
    /// 重複処理防止・トレーサビリティ確保
    /// </summary>
    public long? OriginalOffset { get; set; }

    /// <summary>
    /// ログ出力用JSON変換（バイナリデータは base64 エンコード）
    /// 注意：これは表示用のみ、DLQ内部では元バイナリを保持
    /// </summary>
    public string ToJsonForLogging()
    {
        var logObject = new
        {
            OriginalTopic,
            OriginalPartition,
            OriginalOffset,
            RecordedAt,
            Metadata,
            KeySize = Key?.Length ?? 0,
            ValueSize = Value?.Length ?? 0,
            KeyPreview = Key != null ? Convert.ToBase64String(Key.Take(50).ToArray()) + (Key.Length > 50 ? "..." : "") : null,
            ValuePreview = Value != null ? Convert.ToBase64String(Value.Take(50).ToArray()) + (Value.Length > 50 ? "..." : "") : null
        };

        return JsonSerializer.Serialize(logObject, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// 安全な文字列表現（ログインジェクション防止）
    /// </summary>
    public override string ToString()
    {
        return $"DlqMessage[Topic={OriginalTopic}, Partition={OriginalPartition}, " +
               $"Offset={OriginalOffset}, KeySize={Key?.Length ?? 0}, ValueSize={Value?.Length ?? 0}, " +
               $"Metadata={Metadata.Count} items, RecordedAt={RecordedAt:yyyy-MM-dd HH:mm:ss}Z]";
    }
}

/// <summary>
/// DLQメッセージ構築用ビルダー
/// 設計理念：流暢なAPI、必須項目の強制、メタデータのサニタイゼーション
/// </summary>
public class DlqMessageBuilder
{
    private readonly DlqMessage _message = new();
    private readonly bool _enableSanitization;

    public DlqMessageBuilder(bool enableSanitization = true)
    {
        _enableSanitization = enableSanitization;
    }

    /// <summary>
    /// 元メッセージのバイナリキーを設定
    /// </summary>
    public DlqMessageBuilder WithKey(byte[]? key)
    {
        _message.Key = key;
        return this;
    }

    /// <summary>
    /// 元メッセージのバイナリ値を設定
    /// </summary>
    public DlqMessageBuilder WithValue(byte[]? value)
    {
        _message.Value = value;
        return this;
    }

    /// <summary>
    /// 元トピック情報を設定
    /// </summary>
    public DlqMessageBuilder FromTopic(string topicName, int? partition = null, long? offset = null)
    {
        _message.OriginalTopic = topicName ?? throw new ArgumentNullException(nameof(topicName));
        _message.OriginalPartition = partition;
        _message.OriginalOffset = offset;
        return this;
    }

    /// <summary>
    /// Kafka ConsumeResult から情報を抽出
    /// </summary>
    public DlqMessageBuilder FromConsumeResult<TKey, TValue>(ConsumeResult<TKey, TValue> consumeResult)
    {
        if (consumeResult == null)
            throw new ArgumentNullException(nameof(consumeResult));

        _message.OriginalTopic = consumeResult.Topic;
        _message.OriginalPartition = consumeResult.Partition.Value;
        _message.OriginalOffset = consumeResult.Offset.Value;

        // バイナリデータの抽出（Kafka内部形式のまま保持）
        if (consumeResult.Message.Key != null)
        {
            _message.Key = ExtractBinaryKey(consumeResult.Message.Key);
        }

        if (consumeResult.Message.Value != null)
        {
            _message.Value = ExtractBinaryValue(consumeResult.Message.Value);
        }

        return this;
    }

    /// <summary>
    /// 失敗カテゴリを設定
    /// </summary>
    public DlqMessageBuilder WithFailureCategory(DlqFailureCategory category)
    {
        return WithMetadata(DlqMetadataKeys.FailureCategory, category.ToString());
    }

    /// <summary>
    /// 例外情報を設定
    /// </summary>
    public DlqMessageBuilder WithException(Exception exception)
    {
        if (exception != null)
        {
            WithMetadata(DlqMetadataKeys.ExceptionType, exception.GetType().Name);
            WithMetadata(DlqMetadataKeys.ExceptionMessage, exception.Message);
            
            if (exception.StackTrace != null)
            {
                // スタックトレースは最初の数行のみ（長すぎる場合）
                var stackTrace = string.Join("\n", exception.StackTrace.Split('\n').Take(5));
                WithMetadata(DlqMetadataKeys.StackTrace, stackTrace);
            }
        }
        return this;
    }

    /// <summary>
    /// 単一メタデータ設定（サニタイゼーション付き）
    /// </summary>
    public DlqMessageBuilder WithMetadata(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Metadata key cannot be null or empty", nameof(key));

        var sanitizedValue = _enableSanitization ? SanitizeMetadataValue(value) : value;
        _message.Metadata[key] = sanitizedValue;
        return this;
    }

    /// <summary>
    /// 複数メタデータ一括設定
    /// </summary>
    public DlqMessageBuilder WithMetadata(Dictionary<string, string> metadata)
    {
        if (metadata != null)
        {
            foreach (var kvp in metadata)
            {
                WithMetadata(kvp.Key, kvp.Value);
            }
        }
        return this;
    }

    /// <summary>
    /// リトライ回数を設定
    /// </summary>
    public DlqMessageBuilder WithRetryCount(int retryCount)
    {
        return WithMetadata(DlqMetadataKeys.RetryCount, retryCount.ToString());
    }

    /// <summary>
    /// 送信タイムスタンプを設定
    /// </summary>
    public DlqMessageBuilder WithTimestamp(DateTime timestamp)
    {
        return WithMetadata(DlqMetadataKeys.OriginalTimestamp, timestamp.ToString("O")); // ISO 8601 format
    }

    /// <summary>
    /// DLQメッセージを構築
    /// </summary>
    public DlqMessage Build()
    {
        // 必須項目の検証
        if (string.IsNullOrEmpty(_message.OriginalTopic))
        {
            throw new InvalidOperationException("OriginalTopic is required");
        }

        // デフォルトメタデータの設定
        if (!_message.Metadata.ContainsKey(DlqMetadataKeys.RecordedAt))
        {
            _message.Metadata[DlqMetadataKeys.RecordedAt] = _message.RecordedAt.ToString("O");
        }

        return _message;
    }

    /// <summary>
    /// メタデータ値のサニタイゼーション（ログインジェクション防止）
    /// </summary>
    private string SanitizeMetadataValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        // 改行・タブの除去
        var sanitized = value
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");

        // 長すぎる場合は切り詰め
        if (sanitized.Length > 1000)
        {
            sanitized = sanitized.Substring(0, 997) + "...";
        }

        return sanitized;
    }

    /// <summary>
    /// Kafkaキーからバイナリデータを抽出
    /// </summary>
    private byte[]? ExtractBinaryKey<TKey>(TKey key)
    {
        return key switch
        {
            null => null,
            byte[] bytes => bytes,
            string str => Encoding.UTF8.GetBytes(str),
            _ => Encoding.UTF8.GetBytes(key.ToString() ?? "")
        };
    }

    /// <summary>
    /// Kafka値からバイナリデータを抽出
    /// </summary>
    private byte[]? ExtractBinaryValue<TValue>(TValue value)
    {
        return value switch
        {
            null => null,
            byte[] bytes => bytes,
            string str => Encoding.UTF8.GetBytes(str),
            _ => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))
        };
    }
}

// =============================================================================================
// Configuration & Constants - 設定・定数
// =============================================================================================

/// <summary>
/// DLQ設定オプション（appsettings.json 対応）
/// </summary>
public class DlqOptions
{
    public const string SectionName = "Messaging:Dlq";

    /// <summary>
    /// DLQトピック名
    /// </summary>
    public string Topic { get; set; } = "system.dlq";

    /// <summary>
    /// DLQ機能を有効にするか
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 最大リトライ回数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// DLQ送信タイムアウト（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// メタデータサニタイゼーションを有効にするか
    /// </summary>
    public bool EnableSanitization { get; set; } = true;

    /// <summary>
    /// DLQトピックのパーティション数（自動作成時）
    /// </summary>
    public int Partitions { get; set; } = 3;

    /// <summary>
    /// DLQトピックのレプリケーション係数
    /// </summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// DLQメッセージの保持期間（ミリ秒）
    /// </summary>
    public long RetentionMs { get; set; } = 604800000; // 7 days

    /// <summary>
    /// 設定の検証
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Topic))
            throw new ArgumentException("DLQ Topic name cannot be null or empty");

        if (MaxRetries < 0)
            throw new ArgumentException("MaxRetries must be non-negative");

        if (TimeoutSeconds <= 0)
            throw new ArgumentException("TimeoutSeconds must be positive");

        if (Partitions <= 0)
            throw new ArgumentException("Partitions must be positive");

        if (ReplicationFactor <= 0)
            throw new ArgumentException("ReplicationFactor must be positive");
    }
}

/// <summary>
/// DLQメタデータの定義済みキー
/// </summary>
public static class DlqMetadataKeys
{
    public const string FailureCategory = "dlq.failure.category";
    public const string ExceptionType = "dlq.exception.type";
    public const string ExceptionMessage = "dlq.exception.message";
    public const string StackTrace = "dlq.exception.stacktrace";
    public const string RetryCount = "dlq.retry.count";
    public const string RecordedAt = "dlq.recorded.at";
    public const string OriginalTimestamp = "dlq.original.timestamp";
    public const string ProducerClientId = "dlq.producer.clientid";
    public const string SchemaId = "dlq.schema.id";
    public const string SerializationFormat = "dlq.serialization.format";
}

/// <summary>
/// DLQ失敗カテゴリ
/// </summary>
public enum DlqFailureCategory
{
    ProducerFailure,
    SchemaFailure,
    SerializationFailure,
    NetworkFailure,
    TimeoutFailure,
    AuthenticationFailure,
    QuotaExceeded,
    Unknown
}

// =============================================================================================
// DLQ Writer Interface & Implementation - DLQ出力インターフェース・実装
// =============================================================================================

/// <summary>
/// DLQ出力の抽象インターフェース
/// </summary>
public interface IDlqWriter
{
    /// <summary>
    /// DLQにメッセージを送信
    /// </summary>
    Task<bool> WriteAsync(DlqMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のDLQメッセージを一括送信
    /// </summary>
    Task<int> WriteBatchAsync(IEnumerable<DlqMessage> messages, CancellationToken cancellationToken = default);

    /// <summary>
    /// DLQライターの統計情報取得
    /// </summary>
    DlqWriterStatistics GetStatistics();
}

/// <summary>
/// DLQライター統計情報
/// </summary>
public class DlqWriterStatistics
{
    public long TotalMessagesWritten { get; set; }
    public long TotalWriteFailures { get; set; }
    public TimeSpan AverageWriteLatency { get; set; }
    public DateTime LastWriteAt { get; set; }
    public double SuccessRate => TotalMessagesWritten + TotalWriteFailures > 0 
        ? (double)TotalMessagesWritten / (TotalMessagesWritten + TotalWriteFailures)
        : 0.0;
}

/// <summary>
/// Kafka DLQライター実装
/// 設計原則：確実性重視、フェイルセーフ、詳細ログ
/// </summary>
public class DlqWriter : IDlqWriter, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly DlqOptions _options;
    private readonly ILogger<DlqWriter> _logger;
    private readonly DlqWriterStatistics _statistics = new();
    private bool _disposed = false;

    public DlqWriter(
        IProducer<string, byte[]> producer,
        IOptions<DlqOptions> options,
        ILogger<DlqWriter> logger)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options.Validate();
    }

    /// <summary>
    /// DLQに単一メッセージを送信
    /// </summary>
    public async Task<bool> WriteAsync(DlqMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        if (!_options.Enabled)
        {
            _logger.LogDebug("DLQ is disabled, skipping message write");
            return true; // DLQが無効な場合は成功として扱う
        }

        var startTime = DateTime.UtcNow;

        try
        {
            // DLQメッセージをKafkaメッセージに変換
            var kafkaMessage = ConvertToKafkaMessage(message);

            // タイムアウト設定
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // Kafka送信
            var deliveryResult = await _producer.ProduceAsync(_options.Topic, kafkaMessage, combinedCts.Token);

            // 統計更新
            var latency = DateTime.UtcNow - startTime;
            UpdateStatistics(true, latency);

            _logger.LogInformation(
                "DLQ message written successfully: Topic={DlqTopic}, Partition={Partition}, Offset={Offset}, " +
                "OriginalTopic={OriginalTopic}, Latency={LatencyMs}ms",
                deliveryResult.Topic, deliveryResult.Partition.Value, deliveryResult.Offset.Value,
                message.OriginalTopic, latency.TotalMilliseconds);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("DLQ write operation was cancelled: OriginalTopic={OriginalTopic}", message.OriginalTopic);
            UpdateStatistics(false, DateTime.UtcNow - startTime);
            return false;
        }
        catch (Exception ex)
        {
            var latency = DateTime.UtcNow - startTime;
            UpdateStatistics(false, latency);

            _logger.LogError(ex,
                "Failed to write message to DLQ: OriginalTopic={OriginalTopic}, " +
                "OriginalPartition={OriginalPartition}, Latency={LatencyMs}ms, " +
                "DlqMessage={DlqMessage}",
                message.OriginalTopic, message.OriginalPartition, latency.TotalMilliseconds,
                message.ToString());

            return false;
        }
    }

    /// <summary>
    /// DLQに複数メッセージを一括送信
    /// </summary>
    public async Task<int> WriteBatchAsync(IEnumerable<DlqMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages == null)
            throw new ArgumentNullException(nameof(messages));

        if (!_options.Enabled)
        {
            _logger.LogDebug("DLQ is disabled, skipping batch write");
            return 0;
        }

        var messageList = messages.ToList();
        if (!messageList.Any())
        {
            return 0;
        }

        var successCount = 0;
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Starting DLQ batch write: {MessageCount} messages", messageList.Count);

        foreach (var message in messageList)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("DLQ batch write cancelled after {SuccessCount}/{TotalCount} messages",
                    successCount, messageList.Count);
                break;
            }

            var success = await WriteAsync(message, cancellationToken);
            if (success)
            {
                successCount++;
            }
        }

        var totalLatency = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "DLQ batch write completed: {SuccessCount}/{TotalCount} messages written, " +
            "TotalLatency={TotalLatencyMs}ms",
            successCount, messageList.Count, totalLatency.TotalMilliseconds);

        return successCount;
    }

    /// <summary>
    /// DLQライター統計情報取得
    /// </summary>
    public DlqWriterStatistics GetStatistics()
    {
        return new DlqWriterStatistics
        {
            TotalMessagesWritten = _statistics.TotalMessagesWritten,
            TotalWriteFailures = _statistics.TotalWriteFailures,
            AverageWriteLatency = _statistics.AverageWriteLatency,
            LastWriteAt = _statistics.LastWriteAt
        };
    }

    /// <summary>
    /// DLQメッセージをKafkaメッセージに変換
    /// </summary>
    private Message<string, byte[]> ConvertToKafkaMessage(DlqMessage dlqMessage)
    {
        // キーは元トピック名_パーティション番号の形式
        var messageKey = dlqMessage.OriginalPartition.HasValue
            ? $"{dlqMessage.OriginalTopic}_{dlqMessage.OriginalPartition.Value}"
            : dlqMessage.OriginalTopic;

        // 値は元のバイナリデータをそのまま使用（重要：JSON変換しない）
        var messageValue = dlqMessage.Value;

        // ヘッダーにメタデータを設定
        var headers = new Headers();
        foreach (var metadata in dlqMessage.Metadata)
        {
            headers.Add(metadata.Key, Encoding.UTF8.GetBytes(metadata.Value));
        }

        // 元キーもヘッダーに保存（復元用）
        if (dlqMessage.Key != null)
        {
            headers.Add("dlq.original.key", dlqMessage.Key);
        }

        return new Message<string, byte[]>
        {
            Key = messageKey,
            Value = messageValue,
            Headers = headers,
            Timestamp = new Timestamp(dlqMessage.RecordedAt)
        };
    }

    /// <summary>
    /// 統計情報更新
    /// </summary>
    private void UpdateStatistics(bool success, TimeSpan latency)
    {
        lock (_statistics)
        {
            if (success)
            {
                _statistics.TotalMessagesWritten++;
            }
            else
            {
                _statistics.TotalWriteFailures++;
            }

            // 移動平均でレイテンシを更新
            if (_statistics.AverageWriteLatency == TimeSpan.Zero)
            {
                _statistics.AverageWriteLatency = latency;
            }
            else
            {
                var totalOperations = _statistics.TotalMessagesWritten + _statistics.TotalWriteFailures;
                var newAverage = (_statistics.AverageWriteLatency.TotalMilliseconds * (totalOperations - 1) + latency.TotalMilliseconds) / totalOperations;
                _statistics.AverageWriteLatency = TimeSpan.FromMilliseconds(newAverage);
            }

            _statistics.LastWriteAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                _producer?.Flush(TimeSpan.FromSeconds(5));
                _producer?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing DLQ writer");
            }
            _disposed = true;
        }
    }
}

// =============================================================================================
// Producer Extensions - Producer拡張メソッド
// =============================================================================================

/// <summary>
/// Producer向けDLQフォールバック拡張メソッド
/// </summary>
public static class DlqProducerExtensions
{
    /// <summary>
    /// DLQフォールバック付きの安全な送信
    /// </summary>
    public static async Task<(bool success, DeliveryResult<TKey, TValue>? result)> TrySendAsync<TKey, TValue>(
        this IProducer<TKey, TValue> producer,
        string topic,
        Message<TKey, TValue> message,
        IDlqWriter dlqWriter,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await producer.ProduceAsync(topic, message, cancellationToken);
            return (true, result);
        }
        catch (Exception ex)
        {
            // DLQに失敗メッセージを送信
            var dlqMessage = new DlqMessageBuilder()
                .WithKey(SerializeKey(message.Key))
                .WithValue(SerializeValue(message.Value))
                .FromTopic(topic)
                .WithFailureCategory(DlqFailureCategory.ProducerFailure)
                .WithException(ex)
                .WithTimestamp(message.Timestamp.UtcDateTime)
                .Build();

            await dlqWriter.WriteAsync(dlqMessage, cancellationToken);

            return (false, null);
        }
    }

    /// <summary>
    /// エンティティ送信（型安全版）
    /// </summary>
    public static async Task<(bool success, DeliveryResult<TKey, TValue>? result)> SendWithDlqAsync<TKey, TValue>(
        this IProducer<TKey, TValue> producer,
        TValue entity,
        IDlqWriter dlqWriter,
        string? topicName = null,
        TKey? key = default,
        CancellationToken cancellationToken = default) where TValue : class
    {
        var topic = topicName ?? typeof(TValue).Name.ToLowerInvariant();
        
        var message = new Message<TKey, TValue>
        {
            Key = key,
            Value = entity,
            Timestamp = Timestamp.Default
        };

        return await producer.TrySendAsync(topic, message, dlqWriter, cancellationToken);
    }

    private static byte[]? SerializeKey<TKey>(TKey key)
    {
        return key switch
        {
            null => null,
            byte[] bytes => bytes,
            string str => Encoding.UTF8.GetBytes(str),
            _ => Encoding.UTF8.GetBytes(key.ToString() ?? "")
        };
    }

    private static byte[]? SerializeValue<TValue>(TValue value)
    {
        return value switch
        {
            null => null,
            byte[] bytes => bytes,
            string str => Encoding.UTF8.GetBytes(str),
            _ => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))
        };
    }
}

// =============================================================================================
// Dependency Injection Extensions - DI拡張
// =============================================================================================

/// <summary>
/// DLQサービス登録用拡張メソッド
/// </summary>
public static class DlqServiceCollectionExtensions
{
    /// <summary>
    /// DLQサービスをDIコンテナに登録
    /// </summary>
    public static IServiceCollection AddDlq(this IServiceCollection services, Action<DlqOptions>? configure = null)
    {
        // 設定登録
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            // appsettings.json から設定を読み込み
            services.AddOptions<DlqOptions>()
                .BindConfiguration(DlqOptions.SectionName)
                .ValidateDataAnnotations()
                .Validate(options =>
                {
                    options.Validate();
                    return true;
                }, "DLQ configuration validation failed");
        }

        // DLQ用Producer登録
        services.AddSingleton<IProducer<string, byte[]>>(provider =>
        {
            var config = new ProducerConfig
            {
                BootstrapServers = "localhost:9092", // TODO: 設定から取得
                ClientId = "dlq-writer",
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 1000
            };

            return new ProducerBuilder<string, byte[]>(config).Build();
        });

        // DLQライター登録
        services.AddSingleton<IDlqWriter, DlqWriter>();

        return services;
    }

    /// <summary>
    /// DLQサービスをKafka設定と統合して登録
    /// </summary>
    public static IServiceCollection AddDlqWithKafkaConfig(
        this IServiceCollection services, 
        string bootstrapServers,
        Action<DlqOptions>? configureDlq = null,
        Action<ProducerConfig>? configureProducer = null)
    {
        // DLQ設定
        if (configureDlq != null)
        {
            services.Configure(configureDlq);
        }
        else
        {
            services.AddOptions<DlqOptions>()
                .BindConfiguration(DlqOptions.SectionName)
                .ValidateDataAnnotations();
        }

        // カスタムProducer設定
        services.AddSingleton<IProducer<string, byte[]>>(provider =>
        {
            var config = new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                ClientId = "dlq-writer",
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageSendMaxRetries = 3,
                RetryBackoffMs = 1000,
                RequestTimeoutMs = 30000,
                CompressionType = CompressionType.Snappy
            };

            // カスタム設定適用
            configureProducer?.Invoke(config);

            return new ProducerBuilder<string, byte[]>(config)
                .SetErrorHandler((_, error) =>
                {
                    var logger = provider.GetService<ILogger<DlqWriter>>();
                    logger?.LogError("DLQ Producer error: {ErrorCode} - {ErrorReason}", error.Code, error.Reason);
                })
                .Build();
        });

        services.AddSingleton<IDlqWriter, DlqWriter>();

        return services;
    }
}

// =============================================================================================
// Advanced DLQ Components - 高度なDLQコンポーネント
// =============================================================================================

/// <summary>
/// DLQメッセージの復旧・再処理支援クラス
/// 設計理念：バイナリデータの完全復元、メタデータによる柔軟な処理制御
/// </summary>
public class DlqRecoveryService
{
    private readonly IConsumer<string, byte[]> _dlqConsumer;
    private readonly ILogger<DlqRecoveryService> _logger;

    public DlqRecoveryService(
        IConsumer<string, byte[]> dlqConsumer,
        ILogger<DlqRecoveryService> logger)
    {
        _dlqConsumer = dlqConsumer ?? throw new ArgumentNullException(nameof(dlqConsumer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// DLQから失敗メッセージを復元
    /// </summary>
    public async Task<List<DlqRecoveredMessage>> RecoverMessagesAsync(
        string dlqTopic,
        TimeSpan? maxAge = null,
        DlqFailureCategory? filterByCategory = null,
        CancellationToken cancellationToken = default)
    {
        var recoveredMessages = new List<DlqRecoveredMessage>();
        var startTime = DateTime.UtcNow;
        var cutoffTime = maxAge.HasValue ? DateTime.UtcNow.Subtract(maxAge.Value) : DateTime.MinValue;

        _logger.LogInformation(
            "Starting DLQ message recovery: Topic={DlqTopic}, MaxAge={MaxAge}, FilterCategory={FilterCategory}",
            dlqTopic, maxAge, filterByCategory);

        try
        {
            _dlqConsumer.Subscribe(dlqTopic);

            while (!cancellationToken.IsCancellationRequested)
            {
                var consumeResult = _dlqConsumer.Consume(TimeSpan.FromSeconds(5));
                
                if (consumeResult == null)
                {
                    _logger.LogDebug("No more messages in DLQ, stopping recovery");
                    break;
                }

                if (consumeResult.IsPartitionEOF)
                {
                    _logger.LogDebug("Reached end of partition {Partition}", consumeResult.Partition.Value);
                    continue;
                }

                try
                {
                    var recoveredMessage = ConvertFromKafkaMessage(consumeResult);
                    
                    // 年齢フィルター
                    if (recoveredMessage.RecordedAt < cutoffTime)
                    {
                        _logger.LogDebug("Skipping old message: RecordedAt={RecordedAt}", recoveredMessage.RecordedAt);
                        continue;
                    }

                    // カテゴリフィルター
                    if (filterByCategory.HasValue)
                    {
                        if (!recoveredMessage.Metadata.TryGetValue(DlqMetadataKeys.FailureCategory, out var categoryStr) ||
                            !Enum.TryParse<DlqFailureCategory>(categoryStr, out var category) ||
                            category != filterByCategory.Value)
                        {
                            continue;
                        }
                    }

                    recoveredMessages.Add(recoveredMessage);
                    
                    _logger.LogDebug(
                        "Recovered DLQ message: OriginalTopic={OriginalTopic}, RecordedAt={RecordedAt}, " +
                        "KeySize={KeySize}, ValueSize={ValueSize}",
                        recoveredMessage.OriginalTopic, recoveredMessage.RecordedAt,
                        recoveredMessage.OriginalKey?.Length ?? 0, recoveredMessage.OriginalValue?.Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, 
                        "Failed to convert DLQ message: Partition={Partition}, Offset={Offset}",
                        consumeResult.Partition.Value, consumeResult.Offset.Value);
                }
            }
        }
        finally
        {
            _dlqConsumer.Unsubscribe();
        }

        var duration = DateTime.UtcNow - startTime;
        _logger.LogInformation(
            "DLQ message recovery completed: {RecoveredCount} messages recovered in {DurationMs}ms",
            recoveredMessages.Count, duration.TotalMilliseconds);

        return recoveredMessages;
    }

    /// <summary>
    /// KafkaメッセージからDLQ復旧メッセージに変換
    /// </summary>
    private DlqRecoveredMessage ConvertFromKafkaMessage(ConsumeResult<string, byte[]> consumeResult)
    {
        var message = consumeResult.Message;
        var metadata = new Dictionary<string, string>();

        // ヘッダーからメタデータを復元
        if (message.Headers != null)
        {
            foreach (var header in message.Headers)
            {
                if (header.Key != "dlq.original.key") // 元キーは別途処理
                {
                    metadata[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
                }
            }
        }

        // 元キーの復元
        byte[]? originalKey = null;
        if (message.Headers?.TryGetLastBytes("dlq.original.key", out var keyBytes) == true)
        {
            originalKey = keyBytes;
        }

        // RecordedAtの復元
        var recordedAt = DateTime.UtcNow;
        if (metadata.TryGetValue(DlqMetadataKeys.RecordedAt, out var recordedAtStr) &&
            DateTime.TryParse(recordedAtStr, out var parsedTime))
        {
            recordedAt = parsedTime;
        }

        // OriginalTopicの復元（メタデータまたはキーから）
        var originalTopic = "";
        if (metadata.TryGetValue("dlq.original.topic", out var topicFromMetadata))
        {
            originalTopic = topicFromMetadata;
        }
        else if (message.Key?.Contains('_') == true)
        {
            // キーから推測（topic_partition形式の場合）
            var parts = message.Key.Split('_');
            originalTopic = string.Join("_", parts.Take(parts.Length - 1));
        }

        return new DlqRecoveredMessage
        {
            OriginalKey = originalKey,
            OriginalValue = message.Value,
            OriginalTopic = originalTopic,
            Metadata = metadata,
            RecordedAt = recordedAt,
            DlqPartition = consumeResult.Partition.Value,
            DlqOffset = consumeResult.Offset.Value,
            DlqTimestamp = consumeResult.Message.Timestamp.UtcDateTime
        };
    }
}

/// <summary>
/// DLQから復旧されたメッセージ
/// </summary>
public class DlqRecoveredMessage
{
    /// <summary>
    /// 復旧された元キー（バイナリ）
    /// </summary>
    public byte[]? OriginalKey { get; set; }

    /// <summary>
    /// 復旧された元値（バイナリ）
    /// </summary>
    public byte[]? OriginalValue { get; set; }

    /// <summary>
    /// 元トピック名
    /// </summary>
    public string OriginalTopic { get; set; } = string.Empty;

    /// <summary>
    /// メタデータ
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// DLQ記録日時
    /// </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>
    /// DLQでのパーティション
    /// </summary>
    public int DlqPartition { get; set; }

    /// <summary>
    /// DLQでのオフセット
    /// </summary>
    public long DlqOffset { get; set; }

    /// <summary>
    /// DLQでのタイムスタンプ
    /// </summary>
    public DateTime DlqTimestamp { get; set; }

    /// <summary>
    /// 失敗カテゴリ取得
    /// </summary>
    public DlqFailureCategory GetFailureCategory()
    {
        if (Metadata.TryGetValue(DlqMetadataKeys.FailureCategory, out var categoryStr) &&
            Enum.TryParse<DlqFailureCategory>(categoryStr, out var category))
        {
            return category;
        }
        return DlqFailureCategory.Unknown;
    }

    /// <summary>
    /// リトライ回数取得
    /// </summary>
    public int GetRetryCount()
    {
        if (Metadata.TryGetValue(DlqMetadataKeys.RetryCount, out var retryCountStr) &&
            int.TryParse(retryCountStr, out var retryCount))
        {
            return retryCount;
        }
        return 0;
    }

    /// <summary>
    /// 元メッセージの再送信
    /// </summary>
    public async Task<bool> ResendAsync<TKey, TValue>(
        IProducer<TKey, TValue> producer,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // バイナリデータを元の型に逆シリアライズ（簡易実装）
            TKey? key = default;
            if (OriginalKey != null)
            {
                key = DeserializeKey<TKey>(OriginalKey);
            }

            TValue? value = default;
            if (OriginalValue != null)
            {
                value = DeserializeValue<TValue>(OriginalValue);
            }

            var message = new Message<TKey, TValue>
            {
                Key = key,
                Value = value
            };

            await producer.ProduceAsync(OriginalTopic, message, cancellationToken);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private TKey? DeserializeKey<TKey>(byte[] keyBytes)
    {
        if (typeof(TKey) == typeof(string))
        {
            return (TKey)(object)Encoding.UTF8.GetString(keyBytes);
        }
        if (typeof(TKey) == typeof(byte[]))
        {
            return (TKey)(object)keyBytes;
        }
        // 他の型の場合はJSON逆シリアライズを試行
        try
        {
            var json = Encoding.UTF8.GetString(keyBytes);
            return JsonSerializer.Deserialize<TKey>(json);
        }
        catch
        {
            return default;
        }
    }

    private TValue? DeserializeValue<TValue>(byte[] valueBytes)
    {
        if (typeof(TValue) == typeof(string))
        {
            return (TValue)(object)Encoding.UTF8.GetString(valueBytes);
        }
        if (typeof(TValue) == typeof(byte[]))
        {
            return (TValue)(object)valueBytes;
        }
        // 他の型の場合はJSON逆シリアライズを試行
        try
        {
            var json = Encoding.UTF8.GetString(valueBytes);
            return JsonSerializer.Deserialize<TValue>(json);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// ログ用JSON表現
    /// </summary>
    public string ToJsonForLogging()
    {
        var logObject = new
        {
            OriginalTopic,
            RecordedAt,
            DlqPartition,
            DlqOffset,
            DlqTimestamp,
            Metadata,
            OriginalKeySize = OriginalKey?.Length ?? 0,
            OriginalValueSize = OriginalValue?.Length ?? 0,
            FailureCategory = GetFailureCategory().ToString(),
            RetryCount = GetRetryCount()
        };

        return JsonSerializer.Serialize(logObject, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

// =============================================================================================
// DLQ Monitoring & Analytics - DLQ監視・分析
// =============================================================================================

/// <summary>
/// DLQ監視・分析サービス
/// </summary>
public class DlqAnalyticsService
{
    private readonly IConsumer<string, byte[]> _dlqConsumer;
    private readonly ILogger<DlqAnalyticsService> _logger;

    public DlqAnalyticsService(
        IConsumer<string, byte[]> dlqConsumer,
        ILogger<DlqAnalyticsService> logger)
    {
        _dlqConsumer = dlqConsumer ?? throw new ArgumentNullException(nameof(dlqConsumer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// DLQ統計情報を取得
    /// </summary>
    public async Task<DlqAnalytics> GetAnalyticsAsync(
        string dlqTopic,
        TimeSpan? timeWindow = null,
        CancellationToken cancellationToken = default)
    {
        var analytics = new DlqAnalytics();
        var cutoffTime = timeWindow.HasValue ? DateTime.UtcNow.Subtract(timeWindow.Value) : DateTime.MinValue;

        _logger.LogInformation("Starting DLQ analytics: Topic={DlqTopic}, TimeWindow={TimeWindow}", 
            dlqTopic, timeWindow);

        try
        {
            _dlqConsumer.Subscribe(dlqTopic);

            while (!cancellationToken.IsCancellationRequested)
            {
                var consumeResult = _dlqConsumer.Consume(TimeSpan.FromSeconds(5));
                
                if (consumeResult == null || consumeResult.IsPartitionEOF)
                    break;

                try
                {
                    var message = ParseDlqMessage(consumeResult);
                    
                    if (message.RecordedAt < cutoffTime)
                        continue;

                    analytics.ProcessMessage(message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse DLQ message for analytics");
                    analytics.ParseErrors++;
                }
            }
        }
        finally
        {
            _dlqConsumer.Unsubscribe();
        }

        analytics.CalculateRates();
        _logger.LogInformation("DLQ analytics completed: {TotalMessages} messages analyzed", 
            analytics.TotalMessages);

        return analytics;
    }

    private DlqMessageSummary ParseDlqMessage(ConsumeResult<string, byte[]> consumeResult)
    {
        var metadata = new Dictionary<string, string>();
        
        if (consumeResult.Message.Headers != null)
        {
            foreach (var header in consumeResult.Message.Headers.Where(h => h.Key != "dlq.original.key"))
            {
                metadata[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
            }
        }

        var recordedAt = DateTime.UtcNow;
        if (metadata.TryGetValue(DlqMetadataKeys.RecordedAt, out var recordedAtStr) &&
            DateTime.TryParse(recordedAtStr, out var parsedTime))
        {
            recordedAt = parsedTime;
        }

        var failureCategory = DlqFailureCategory.Unknown;
        if (metadata.TryGetValue(DlqMetadataKeys.FailureCategory, out var categoryStr) &&
            Enum.TryParse<DlqFailureCategory>(categoryStr, out var category))
        {
            failureCategory = category;
        }

        var originalTopic = "";
        if (metadata.TryGetValue("dlq.original.topic", out var topicFromMetadata))
        {
            originalTopic = topicFromMetadata;
        }
        else if (consumeResult.Message.Key?.Contains('_') == true)
        {
            var parts = consumeResult.Message.Key.Split('_');
            originalTopic = string.Join("_", parts.Take(parts.Length - 1));
        }

        return new DlqMessageSummary
        {
            OriginalTopic = originalTopic,
            FailureCategory = failureCategory,
            RecordedAt = recordedAt,
            MessageSize = (consumeResult.Message.Value?.Length ?? 0) + (consumeResult.Message.Key?.Length ?? 0),
            Metadata = metadata
        };
    }
}

/// <summary>
/// DLQ分析結果
/// </summary>
public class DlqAnalytics
{
    public int TotalMessages { get; set; }
    public int ParseErrors { get; set; }
    public Dictionary<string, int> MessagesByTopic { get; set; } = new();
    public Dictionary<DlqFailureCategory, int> MessagesByCategory { get; set; } = new();
    public Dictionary<DateTime, int> MessagesByHour { get; set; } = new();
    public long TotalMessageSize { get; set; }
    public DateTime EarliestMessage { get; set; } = DateTime.MaxValue;
    public DateTime LatestMessage { get; set; } = DateTime.MinValue;
    public double MessagesPerHour { get; set; }
    public double AverageMessageSize { get; set; }

    public void ProcessMessage(DlqMessageSummary message)
    {
        TotalMessages++;
        TotalMessageSize += message.MessageSize;

        // トピック別集計
        if (!string.IsNullOrEmpty(message.OriginalTopic))
        {
            MessagesByTopic[message.OriginalTopic] = MessagesByTopic.GetValueOrDefault(message.OriginalTopic, 0) + 1;
        }

        // カテゴリ別集計
        MessagesByCategory[message.FailureCategory] = MessagesByCategory.GetValueOrDefault(message.FailureCategory, 0) + 1;

        // 時間別集計
        var hourKey = new DateTime(message.RecordedAt.Year, message.RecordedAt.Month, message.RecordedAt.Day, message.RecordedAt.Hour, 0, 0);
        MessagesByHour[hourKey] = MessagesByHour.GetValueOrDefault(hourKey, 0) + 1;

        // 時間範囲更新
        if (message.RecordedAt < EarliestMessage)
            EarliestMessage = message.RecordedAt;
        if (message.RecordedAt > LatestMessage)
            LatestMessage = message.RecordedAt;
    }

    public void CalculateRates()
    {
        if (TotalMessages > 0)
        {
            AverageMessageSize = (double)TotalMessageSize / TotalMessages;

            if (EarliestMessage != DateTime.MaxValue && LatestMessage != DateTime.MinValue)
            {
                var timeSpan = LatestMessage - EarliestMessage;
                if (timeSpan.TotalHours > 0)
                {
                    MessagesPerHour = TotalMessages / timeSpan.TotalHours;
                }
            }
        }
    }

    public string ToJsonReport()
    {
        var report = new
        {
            Summary = new
            {
                TotalMessages,
                ParseErrors,
                TotalMessageSize,
                AverageMessageSize,
                MessagesPerHour,
                EarliestMessage,
                LatestMessage
            },
            ByTopic = MessagesByTopic.OrderByDescending(x => x.Value).ToDictionary(x => x.Key, x => x.Value),
            ByCategory = MessagesByCategory.ToDictionary(x => x.Key.ToString(), x => x.Value),
            ByHour = MessagesByHour.OrderBy(x => x.Key).ToDictionary(x => x.Key.ToString("yyyy-MM-dd HH:00"), x => x.Value)
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

/// <summary>
/// DLQメッセージ要約情報（分析用）
/// </summary>
public class DlqMessageSummary
{
    public string OriginalTopic { get; set; } = string.Empty;
    public DlqFailureCategory FailureCategory { get; set; }
    public DateTime RecordedAt { get; set; }
    public int MessageSize { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}