using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;

namespace KsqlDsl.Configuration;



public class MergedTopicConfig
{
    public Type EntityType { get; set; } = null!;

    public TopicAttribute? OriginalAttribute { get; set; }

    public TopicOverride? Override { get; set; }

    public string TopicName { get; set; } = string.Empty;

    public int PartitionCount { get; set; }

    public int ReplicationFactor { get; set; }

    public long RetentionMs { get; set; }

    public bool Compaction { get; set; }

    public bool DeadLetterQueue { get; set; }

    public int? MaxMessageBytes { get; set; }

    public long? SegmentBytes { get; set; }

    public Dictionary<string, object> CustomKafkaConfig { get; set; } = new();

    public bool HasOverride { get; set; }

    public string? OverrideReason { get; set; }

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

    public override string ToString()
    {
        var overrideStatus = HasOverride ? $" [上書き済み: {OverrideReason}]" : " [属性値使用]";
        return $"Topic: {TopicName}, Partitions: {PartitionCount}, Replicas: {ReplicationFactor}, Retention: {RetentionMs}ms{overrideStatus}";
    }
}