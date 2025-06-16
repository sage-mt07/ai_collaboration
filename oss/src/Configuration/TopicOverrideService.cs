using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace KsqlDsl.Configuration;

public class TopicOverrideService
{
    private readonly Dictionary<Type, TopicOverride> _overrides = new();

    public void OverrideTopicOption<T>(TopicOverride overrideConfig)
    {
        if (overrideConfig == null)
            throw new ArgumentNullException(nameof(overrideConfig));

        _overrides[typeof(T)] = overrideConfig;
    }

    public void OverrideTopicOption<T>(int? partitionCount = null, int? replicationFactor = null, long? retentionMs = null, string? reason = null)
    {
        var overrideConfig = new TopicOverride
        {
            PartitionCount = partitionCount,
            ReplicationFactor = replicationFactor,
            RetentionMs = retentionMs,
            Reason = reason
        };

        OverrideTopicOption<T>(overrideConfig);
    }

    public void RemoveOverride<T>()
    {
        _overrides.Remove(typeof(T));
    }

    public void ClearAllOverrides()
    {
        _overrides.Clear();
    }

    public MergedTopicConfig GetMergedTopicConfig(Type entityType)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));

        var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();
        var hasOverride = _overrides.TryGetValue(entityType, out var overrideConfig);

        return new MergedTopicConfig
        {
            EntityType = entityType,
            OriginalAttribute = topicAttribute,
            Override = overrideConfig,
            TopicName = topicAttribute?.TopicName ?? entityType.Name,
            PartitionCount = overrideConfig?.PartitionCount ?? topicAttribute?.PartitionCount ?? 1,
            ReplicationFactor = overrideConfig?.ReplicationFactor ?? topicAttribute?.ReplicationFactor ?? 1,
            RetentionMs = overrideConfig?.RetentionMs ?? topicAttribute?.RetentionMs ?? 604800000,
            Compaction = overrideConfig?.Compaction ?? topicAttribute?.Compaction ?? false,
            DeadLetterQueue = overrideConfig?.DeadLetterQueue ?? topicAttribute?.DeadLetterQueue ?? false,
            MaxMessageBytes = overrideConfig?.MaxMessageBytes ?? topicAttribute?.MaxMessageBytes,
            SegmentBytes = overrideConfig?.SegmentBytes ?? topicAttribute?.SegmentBytes,
            CustomKafkaConfig = overrideConfig?.CustomKafkaConfig ?? new Dictionary<string, object>(),
            HasOverride = hasOverride,
            OverrideReason = overrideConfig?.Reason
        };
    }

    public Dictionary<Type, TopicOverride> GetAllOverrides()
    {
        return new Dictionary<Type, TopicOverride>(_overrides);
    }

    public string GetOverrideSummary()
    {
        if (_overrides.Count == 0)
            return "トピック上書き設定: なし";

        var summary = new List<string> { "トピック上書き設定:" };
        foreach (var kvp in _overrides)
        {
            var entityName = kvp.Key.Name;
            var config = kvp.Value;
            var reason = string.IsNullOrEmpty(config.Reason) ? "理由未記載" : config.Reason;

            var details = new List<string>();
            if (config.PartitionCount.HasValue) details.Add($"Partitions: {config.PartitionCount}");
            if (config.ReplicationFactor.HasValue) details.Add($"Replicas: {config.ReplicationFactor}");
            if (config.RetentionMs.HasValue) details.Add($"Retention: {config.RetentionMs}ms");
            if (config.Compaction.HasValue) details.Add($"Compaction: {config.Compaction}");
            if (config.DeadLetterQueue.HasValue) details.Add($"DLQ: {config.DeadLetterQueue}");

            var detailsStr = details.Count > 0 ? string.Join(", ", details) : "設定なし";
            summary.Add($"  - {entityName}: {detailsStr} (理由: {reason})");
        }

        return string.Join(Environment.NewLine, summary);
    }
}