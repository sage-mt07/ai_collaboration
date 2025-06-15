using KsqlDsl.Configuration;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Options;

/// <summary>
/// KafkaContextオプションビルダー
/// EntityFramework風のFluent API設定システム
/// </summary>
public class KafkaContextOptionsBuilder
{
    private readonly KafkaContextOptions _options = new();

    /// <summary>
    /// Kafka接続設定
    /// </summary>
    /// <param name="connectionString">Kafkaブローカー接続文字列（例: "localhost:9092"）</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentException">接続文字列がnullまたは空の場合</exception>
    public KafkaContextOptionsBuilder UseKafka(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Kafka接続文字列は必須です", nameof(connectionString));

        _options.ConnectionString = connectionString;
        return this;
    }

    /// <summary>
    /// Schema Registry接続設定
    /// </summary>
    /// <param name="schemaRegistryUrl">Schema Registry接続URL（例: "http://localhost:8081"）</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentException">URLがnullまたは空の場合</exception>
    public KafkaContextOptionsBuilder UseSchemaRegistry(string schemaRegistryUrl)
    {
        if (string.IsNullOrWhiteSpace(schemaRegistryUrl))
            throw new ArgumentException("Schema Registry URLは必須です", nameof(schemaRegistryUrl));

        _options.SchemaRegistryUrl = schemaRegistryUrl;
        return this;
    }

    /// <summary>
    /// ゆるめバリデーションモードを有効化
    /// 学習・PoC用途限定。属性なしでも警告付きで動作
    /// </summary>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder EnableRelaxedValidation()
    {
        _options.ValidationMode = ValidationMode.Relaxed;
        return this;
    }

    /// <summary>
    /// 厳格バリデーションモードを有効化（デフォルト）
    /// 属性未定義時は例外で停止
    /// </summary>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder EnableStrictValidation()
    {
        _options.ValidationMode = ValidationMode.Strict;
        return this;
    }

    /// <summary>
    /// トピック運用パラメータ上書き設定
    /// </summary>
    /// <typeparam name="T">対象エンティティタイプ</typeparam>
    /// <param name="overrideConfig">上書き設定</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentNullException">上書き設定がnullの場合</exception>
    public KafkaContextOptionsBuilder OverrideTopicOption<T>(TopicOverride overrideConfig)
    {
        if (overrideConfig == null)
            throw new ArgumentNullException(nameof(overrideConfig));

        _options.TopicOverrideService.OverrideTopicOption<T>(overrideConfig);
        return this;
    }

    /// <summary>
    /// トピック運用パラメータ上書き設定（簡易版）
    /// </summary>
    /// <typeparam name="T">対象エンティティタイプ</typeparam>
    /// <param name="partitionCount">パーティション数</param>
    /// <param name="replicationFactor">レプリケーションファクター</param>
    /// <param name="retentionMs">保持期間（ミリ秒）</param>
    /// <param name="reason">上書き理由</param>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder OverrideTopicOption<T>(int? partitionCount = null, int? replicationFactor = null, long? retentionMs = null, string? reason = null)
    {
        _options.TopicOverrideService.OverrideTopicOption<T>(partitionCount, replicationFactor, retentionMs, reason);
        return this;
    }

    /// <summary>
    /// コンシューマーグループID設定
    /// </summary>
    /// <param name="groupId">コンシューマーグループID</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentException">グループIDがnullまたは空の場合</exception>
    public KafkaContextOptionsBuilder UseConsumerGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("コンシューマーグループIDは必須です", nameof(groupId));

        _options.ConsumerGroupId = groupId;
        return this;
    }

    /// <summary>
    /// プロデューサー設定の追加
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="value">設定値</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentException">設定キーがnullまたは空の場合</exception>
    /// <exception cref="ArgumentNullException">設定値がnullの場合</exception>
    public KafkaContextOptionsBuilder ConfigureProducer(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("設定キーは必須です", nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _options.ProducerConfig[key] = value;
        return this;
    }

    /// <summary>
    /// コンシューマー設定の追加
    /// </summary>
    /// <param name="key">設定キー</param>
    /// <param name="value">設定値</param>
    /// <returns>ビルダーインスタンス</returns>
    /// <exception cref="ArgumentException">設定キーがnullまたは空の場合</exception>
    /// <exception cref="ArgumentNullException">設定値がnullの場合</exception>
    public KafkaContextOptionsBuilder ConfigureConsumer(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("設定キーは必須です", nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _options.ConsumerConfig[key] = value;
        return this;
    }

    /// <summary>
    /// デバッグログ出力を有効化
    /// </summary>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder EnableDebugLogging()
    {
        _options.EnableDebugLogging = true;
        return this;
    }

    /// <summary>
    /// 自動トピック作成を無効化
    /// </summary>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder DisableAutoTopicCreation()
    {
        _options.EnableAutoTopicCreation = false;
        return this;
    }

    /// <summary>
    /// 自動スキーマ登録を無効化
    /// </summary>
    /// <returns>ビルダーインスタンス</returns>
    public KafkaContextOptionsBuilder DisableAutoSchemaRegistration()
    {
        _options.EnableAutoSchemaRegistration = false;
        return this;
    }

    /// <summary>
    /// 設定済みオプションを取得
    /// </summary>
    /// <returns>KafkaContextオプション</returns>
    /// <exception cref="InvalidOperationException">必須設定が不足している場合</exception>
    public KafkaContextOptions Build()
    {
        // 必須設定の検証
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Kafka接続文字列が設定されていません。UseKafka()を呼び出してください。");
        }

        return _options.Clone();
    }

    /// <summary>
    /// 現在の設定オプションを取得（内部用）
    /// </summary>
    /// <returns>KafkaContextオプション</returns>
    internal KafkaContextOptions GetOptions()
    {
        return _options;
    }
}