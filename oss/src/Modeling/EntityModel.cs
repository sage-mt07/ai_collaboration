using KsqlDsl.Attributes;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Modeling;

public class EntityModel
{
    /// <summary>
    /// エンティティタイプ
    /// </summary>
    public Type EntityType { get; set; } = null!;

    /// <summary>
    /// [Topic]属性情報
    /// </summary>
    public TopicAttribute? TopicAttribute { get; set; }

    /// <summary>
    /// キープロパティ一覧（[Key]属性付き）
    /// </summary>
    public PropertyInfo[] KeyProperties { get; set; } = Array.Empty<PropertyInfo>();

    /// <summary>
    /// 全プロパティ一覧
    /// </summary>
    public PropertyInfo[] AllProperties { get; set; } = Array.Empty<PropertyInfo>();

    /// <summary>
    /// バリデーション結果
    /// </summary>
    public ValidationResult? ValidationResult { get; set; }

    /// <summary>
    /// モデルが有効かどうか
    /// </summary>
    public bool IsValid => ValidationResult?.IsValid ?? false;
}