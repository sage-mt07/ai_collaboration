using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Configuration;

/// <summary>
/// トピック運用パラメータの上書き設定
/// 属性値は初期値・設計ガイドとして使用し、実際の運用値は外部から上書き可能
/// </summary>
public class TopicOverride
{
    /// <summary>
    /// 上書きするパーティション数（未設定時は属性値を使用）
    /// </summary>
    public int? PartitionCount { get; set; }

    /// <summary>
    /// 上書きするレプリケーションファクター（未設定時は属性値を使用）
    /// </summary>
    public int? ReplicationFactor { get; set; }

    /// <summary>
    /// 上書きする保持期間（ミリ秒）（未設定時は属性値を使用）
    /// </summary>
    public long? RetentionMs { get; set; }

    /// <summary>
    /// 上書きするコンパクション設定（未設定時は属性値を使用）
    /// </summary>
    public bool? Compaction { get; set; }

    /// <summary>
    /// 上書きするデッドレターキュー設定（未設定時は属性値を使用）
    /// </summary>
    public bool? DeadLetterQueue { get; set; }

    /// <summary>
    /// 上書きする最大メッセージサイズ（バイト）（未設定時は属性値を使用）
    /// </summary>
    public int? MaxMessageBytes { get; set; }

    /// <summary>
    /// 上書きするセグメントサイズ（バイト）（未設定時は属性値を使用）
    /// </summary>
    public long? SegmentBytes { get; set; }

    /// <summary>
    /// カスタムKafka設定（key-value形式）
    /// 属性で定義されていない高度な設定を追加する場合に使用
    /// </summary>
    public Dictionary<string, object> CustomKafkaConfig { get; set; } = new();

    /// <summary>
    /// 上書き理由（運用管理・ログ出力用）
    /// </summary>
    public string? Reason { get; set; }
}