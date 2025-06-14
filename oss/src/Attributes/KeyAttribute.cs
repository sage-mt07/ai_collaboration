using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Attributes;

/// <summary>
/// Kafkaメッセージのキーを示す属性
/// POCO属性主導型KafkaContextでキープロパティを定義するために使用
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class KeyAttribute : Attribute
{
    /// <summary>
    /// キーの順序（複合キーの場合に使用）
    /// デフォルト: 0
    /// </summary>
    public int Order { get; set; } = 0;

    /// <summary>
    /// キーのエンコーディング方式（任意）
    /// 例: "UTF-8", "AVRO", "JSON" など
    /// 未指定時はKafkaContextの既定エンコーディングを使用
    /// </summary>
    public string? Encoding { get; set; }

    /// <summary>
    /// 初期化
    /// </summary>
    public KeyAttribute()
    {
    }

    /// <summary>
    /// 初期化（順序指定）
    /// </summary>
    /// <param name="order">キーの順序</param>
    public KeyAttribute(int order)
    {
        Order = order;
    }

    /// <summary>
    /// 設定内容の文字列表現
    /// </summary>
    /// <returns>設定概要</returns>
    public override string ToString()
    {
        var encoding = string.IsNullOrEmpty(Encoding) ? "Default" : Encoding;
        return $"Key (Order: {Order}, Encoding: {encoding})";
    }
}