using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Configuration;


/// <summary>
/// 属性値と上書き設定をマージした最終的なトピック設定
/// </summary>
public class MergedTopicConfig
{
    /// <summary>
    /// 対象エンティティタイプ
    /// </summary>
    public Type EntityType { get; set; } = null!;

    /// <summary>
    /// 元の[Topic]属性（null許可）
    /// </summary>
    public TopicAttribute? OriginalAttribute { get; set; }

    /// <summary>
    /// 上書き設定（null許可）
    /// </summary>
    public TopicOverride? Override { get; set; }

    /// <summary>
    /// 最終的なトピック名
    /// </summary>
    public string TopicName { get; set; } = string.Empty;

    /// <summary>
    /// 最終的なパーティション数
    /// </summary>
    public int PartitionCount { get; set; }

    /// <summary>
    /// 最終的なレプリケーションファクター
    /// </summary>
    public int ReplicationFactor { get; set; }

    /// <summary>
    /// 最終的な保持期間（ミリ秒）
    /// </summary>
    public long RetentionMs { get; set; }

    /// <summary>
    /// 最終的なコンパクション設定
    /// </summary>
    public bool Compaction { get; set; }

    /// <summary>
    /// 最終的なデッドレターキュー設定
    /// </summary>
    public bool DeadLetterQueue { get; set; }

    /// <summary>
    /// 最終的な最大メッセージサイズ（バイト）
    /// </summary>
    public int? MaxMessageBytes { get; set; }

    /// <summary>
    /// 最終的なセグメントサイズ（バイト）
    /// </summary>
    public long? SegmentBytes { get; set; }

    /// <summary>
    /// カスタムKafka設定
    /// </summary>
    public Dictionary<string, object> CustomKafkaConfig { get; set; } = new();

    /// <summary>
    /// 上書き設定が適用されているかどうか
    /// </summary>
    public bool HasOverride { get; set; }

    /// <summary>
    /// 上書き理由
    /// </summary>
    public string? OverrideReason { get; set; }

    /// <summary>
    /// 最終的なKafkaトピック設定を辞書形式で取得
    /// </summary>
    /// <returns>Kafkaトピック設定辞書</returns>
    public Dictionary<string, object> ToFinalKafkaTopicConfig()
    {
        var config = new Dictionary<string, object>
        {
            ["cleanup.policy"] = Compaction ? "compact" : "delete",
            ["retention.ms"] = RetentionMs
        };

        if (MaxMessageBytes.HasValue)
            config["max.message.bytes"] = MaxMessageBytes.Value;

        if (SegmentBytes.HasValue)
            config["segment.bytes"] = SegmentBytes.Value;

        // カスタム設定をマージ（上書き優先）
        foreach (var kvp in CustomKafkaConfig)
        {
            config[kvp.Key] = kvp.Value;
        }

        return config;
    }

    /// <summary>
    /// 設定内容の詳細文字列表現
    /// </summary>
    /// <returns>設定詳細</returns>
    public override string ToString()
    {
        var overrideStatus = HasOverride ? $" [上書き済み: {OverrideReason}]" : " [属性値使用]";
        return $"Topic: {TopicName}, Partitions: {PartitionCount}, Replicas: {ReplicationFactor}, Retention: {RetentionMs}ms{overrideStatus}";
    }
}