using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Attributes;

/// <summary>
/// プロパティのデフォルト値を指定する属性
/// スキーマ定義・初期化値として使用
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class DefaultValueAttribute : Attribute
{
    /// <summary>
    /// デフォルト値
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="value">デフォルト値</param>
    public DefaultValueAttribute(object? value)
    {
        Value = value;
    }

    /// <summary>
    /// 設定内容の文字列表現
    /// </summary>
    /// <returns>設定概要</returns>
    public override string ToString()
    {
        return $"DefaultValue: {Value ?? "null"}";
    }
}