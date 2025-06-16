using System.Collections.Generic;

namespace KsqlDsl.Configuration;

public class TopicOverride
{
    public int? PartitionCount { get; set; }

    public int? ReplicationFactor { get; set; }

    public long? RetentionMs { get; set; }

    public bool? Compaction { get; set; }

    public bool? DeadLetterQueue { get; set; }

    public int? MaxMessageBytes { get; set; }

    public long? SegmentBytes { get; set; }

    public Dictionary<string, object> CustomKafkaConfig { get; set; } = new();

    public string? Reason { get; set; }
}