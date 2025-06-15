using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Configuration;

/// <summary>
/// トピック運用パラメータ上書きサービス
/// POCO属性で定義された設計値を運用環境に応じて上書きする機能を提供
/// </summary>
public class TopicOverrideService
{
    private readonly Dictionary<Type, TopicOverride> _overrides = new();

    /// <summary>
    /// 指定エンティティタイプのトピック設定を上書き登録
    /// </summary>
    /// <typeparam name="T">対象エンティティタイプ</typeparam>
    /// <param name="overrideConfig">上書き設定</param>
    /// <exception cref="ArgumentNullException">上書き設定がnullの場合</exception>
    public void OverrideTopicOption<T>(TopicOverride overrideConfig)
    {
        if (overrideConfig == null)
            throw new ArgumentNullException(nameof(overrideConfig));

        _overrides[typeof(T)] = overrideConfig;
    }

    /// <summary>
    /// 指定エンティティタイプのトピック設定を上書き登録（簡易版）
    /// </summary>
    /// <typeparam name="T">対象エンティティタイプ</typeparam>
    /// <param name="partitionCount">パーティション数</param>
    /// <param name="replicationFactor">レプリケーションファクター</param>
    /// <param name="retentionMs">保持期間（ミリ秒）</param>
    /// <param name="reason">上書き理由</param>
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

    /// <summary>
    /// 指定エンティティタイプの上書き設定を削除
    /// </summary>
    /// <typeparam name="T">対象エンティティタイプ</typeparam>
    public void RemoveOverride<T>()
    {
        _overrides.Remove(typeof(T));
    }

    /// <summary>
    /// 全ての上書き設定をクリア
    /// </summary>
    public void ClearAllOverrides()
    {
        _overrides.Clear();
    }

    /// <summary>
    /// 属性値と上書き設定をマージした最終的なトピック設定を取得
    /// </summary>
    /// <param name="entityType">対象エンティティタイプ</param>
    /// <returns>マージされたトピック設定</returns>
    /// <exception cref="ArgumentNullException">エンティティタイプがnullの場合</exception>
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

    /// <summary>
    /// 登録済み上書き設定の一覧を取得
    /// </summary>
    /// <returns>上書き設定一覧</returns>
    public Dictionary<Type, TopicOverride> GetAllOverrides()
    {
        return new Dictionary<Type, TopicOverride>(_overrides);
    }

    /// <summary>
    /// 上書き設定の詳細をログ出力用文字列で取得
    /// </summary>
    /// <returns>上書き設定詳細</returns>
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