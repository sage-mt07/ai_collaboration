using KsqlDsl.Configuration;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Options;

/// <summary>
/// KafkaContext設定オプション
/// EntityFramework風のオプション設定システム
/// </summary>
public class KafkaContextOptions
{
    /// <summary>
    /// Kafkaブローカー接続文字列
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Schema Registry接続文字列
    /// </summary>
    public string? SchemaRegistryUrl { get; set; }

    /// <summary>
    /// バリデーションモード
    /// </summary>
    public ValidationMode ValidationMode { get; set; } = ValidationMode.Strict;

    /// <summary>
    /// トピック上書きサービス
    /// </summary>
    public TopicOverrideService TopicOverrideService { get; set; } = new();

    /// <summary>
    /// コンシューマーグループID
    /// </summary>
    public string? ConsumerGroupId { get; set; }

    /// <summary>
    /// プロデューサー設定
    /// </summary>
    public Dictionary<string, object> ProducerConfig { get; set; } = new();

    /// <summary>
    /// コンシューマー設定
    /// </summary>
    public Dictionary<string, object> ConsumerConfig { get; set; } = new();

    /// <summary>
    /// デバッグモード有効フラグ
    /// </summary>
    public bool EnableDebugLogging { get; set; } = false;

    /// <summary>
    /// 自動トピック作成有効フラグ
    /// </summary>
    public bool EnableAutoTopicCreation { get; set; } = true;

    /// <summary>
    /// スキーマ自動登録有効フラグ
    /// </summary>
    public bool EnableAutoSchemaRegistration { get; set; } = true;

    /// <summary>
    /// オプション設定のクローンを作成
    /// </summary>
    /// <returns>クローンされたオプション</returns>
    public KafkaContextOptions Clone()
    {
        return new KafkaContextOptions
        {
            ConnectionString = ConnectionString,
            SchemaRegistryUrl = SchemaRegistryUrl,
            ValidationMode = ValidationMode,
            TopicOverrideService = TopicOverrideService, // 参照コピー
            ConsumerGroupId = ConsumerGroupId,
            ProducerConfig = new Dictionary<string, object>(ProducerConfig),
            ConsumerConfig = new Dictionary<string, object>(ConsumerConfig),
            EnableDebugLogging = EnableDebugLogging,
            EnableAutoTopicCreation = EnableAutoTopicCreation,
            EnableAutoSchemaRegistration = EnableAutoSchemaRegistration
        };
    }

    /// <summary>
    /// 設定内容の文字列表現
    /// </summary>
    /// <returns>設定概要</returns>
    public override string ToString()
    {
        var connectionString = ConnectionString ?? "未設定";
        var schemaRegistry = SchemaRegistryUrl ?? "未設定";
        var consumerGroup = ConsumerGroupId ?? "未設定";
        return $"KafkaContext Options: Kafka={connectionString}, SchemaRegistry={schemaRegistry}, Validation={ValidationMode}, ConsumerGroup={consumerGroup}";
    }
}