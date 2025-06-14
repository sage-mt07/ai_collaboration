using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Attributes;
/// <summary>
/// POCO属性主導型KafkaContext用のTopic属性
/// 物理トピック名・各種パラメータをPOCOに一意定義するための属性
/// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TopicAttribute : Attribute
{
    /// <summary>
    /// 物理トピック名（必須）
    /// </summary>
    public string TopicName { get; }

    /// <summary>
    /// パーティション数（デフォルト: 1）
    /// </summary>
    public int PartitionCount { get; set; } = 1;

    /// <summary>
    /// レプリケーションファクター（デフォルト: 1）
    /// </summary>
    public int ReplicationFactor { get; set; } = 1;

    /// <summary>
    /// 保持期間（ミリ秒）（デフォルト: 604800000 = 7日）
    /// </summary>
    public long RetentionMs { get; set; } = 604800000; // 7 days

    /// <summary>
    /// コンパクション有効フラグ（デフォルト: false）
    /// true = log.cleanup.policy=compact, false = log.cleanup.policy=delete
    /// </summary>
    public bool Compaction { get; set; } = false;

    /// <summary>
    /// デッドレターキュー有効フラグ（デフォルト: false）
    /// </summary>
    public bool DeadLetterQueue { get; set; } = false;

    /// <summary>
    /// トピックの説明・用途（任意）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 最大メッセージサイズ（バイト）（任意、未設定時はKafkaデフォルト使用）
    /// </summary>
    public int? MaxMessageBytes { get; set; }

    /// <summary>
    /// セグメントサイズ（バイト）（任意、未設定時はKafkaデフォルト使用）
    /// </summary>
    public long? SegmentBytes { get; set; }

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="topicName">物理トピック名（必須）</param>
    public TopicAttribute(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
            throw new ArgumentException("トピック名は必須です", nameof(topicName));

        TopicName = topicName;
    }

    /// <summary>
    /// 設定内容の文字列表現
    /// </summary>
    /// <returns>設定概要</returns>
    public override string ToString()
    {
        var desc = string.IsNullOrEmpty(Description) ? "" : $" ({Description})";
        return $"Topic: {TopicName}{desc}, Partitions: {PartitionCount}, Replicas: {ReplicationFactor}";
    }

    /// <summary>
    /// Kafkaトピック設定を辞書形式で取得
    /// 運用値上書き用の基準値として使用
    /// </summary>
    /// <returns>Kafkaトピック設定辞書</returns>
    public Dictionary<string, object> ToKafkaTopicConfig()
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

        return config;
    }
}
