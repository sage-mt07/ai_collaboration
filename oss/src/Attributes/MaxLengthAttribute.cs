using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Attributes;

/// <summary>
/// プロパティの最大長を指定する属性
/// 文字列プロパティのスキーマ定義・バリデーションに使用
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public class MaxLengthAttribute : Attribute
{
    /// <summary>
    /// 最大長
    /// </summary>
    public int Length { get; }

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="length">最大長</param>
    /// <exception cref="ArgumentException">長さが0以下の場合</exception>
    public MaxLengthAttribute(int length)
    {
        if (length <= 0)
            throw new ArgumentException("最大長は1以上である必要があります", nameof(length));

        Length = length;
    }

    /// <summary>
    /// 設定内容の文字列表現
    /// </summary>
    /// <returns>設定概要</returns>
    public override string ToString()
    {
        return $"MaxLength: {Length}";
    }
}
