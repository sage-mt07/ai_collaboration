using System;
using System.Collections.Generic;

namespace KsqlDsl.Attributes;
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class TopicAttribute : Attribute
{
    public string TopicName { get; }

    public int PartitionCount { get; set; } = 1;

    public int ReplicationFactor { get; set; } = 1;

    public long RetentionMs { get; set; } = 604800000; // 7 days

    public bool Compaction { get; set; } = false;

    public bool DeadLetterQueue { get; set; } = false;

    public string? Description { get; set; }

    public int? MaxMessageBytes { get; set; }

    public long? SegmentBytes { get; set; }

    public TopicAttribute(string topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
            throw new ArgumentException("トピック名は必須です", nameof(topicName));

        TopicName = topicName;
    }

    public override string ToString()
    {
        var desc = string.IsNullOrEmpty(Description) ? "" : $" ({Description})";
        return $"Topic: {TopicName}{desc}, Partitions: {PartitionCount}, Replicas: {ReplicationFactor}";
    }
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