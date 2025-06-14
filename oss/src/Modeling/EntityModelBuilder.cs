using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Modeling;

/// <summary>
/// エンティティモデルビルダー
/// 将来的なFluent API拡張用（現在はPOCO属性主導のため設定変更禁止）
/// </summary>
/// <typeparam name="T">エンティティタイプ</typeparam>
public class EntityModelBuilder<T> where T : class
{
    private readonly EntityModel _entityModel;

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="entityModel">エンティティモデル</param>
    internal EntityModelBuilder(EntityModel entityModel)
    {
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
    }

    /// <summary>
    /// エンティティモデル情報を取得
    /// </summary>
    /// <returns>エンティティモデル</returns>
    public EntityModel GetModel()
    {
        return _entityModel;
    }

    /// <summary>
    /// トピック名の変更を試行（POCO属性主導では禁止）
    /// </summary>
    /// <param name="topicName">トピック名</param>
    /// <returns>このビルダー</returns>
    /// <exception cref="NotSupportedException">POCO属性主導では物理名変更禁止</exception>
    [Obsolete("POCO属性主導型では、Fluent APIでのトピック名変更は禁止されています。[Topic]属性を使用してください。", true)]
    public EntityModelBuilder<T> HasTopicName(string topicName)
    {
        throw new NotSupportedException("POCO属性主導型では、Fluent APIでのトピック名変更は禁止されています。[Topic]属性を使用してください。");
    }

    /// <summary>
    /// キー設定の変更を試行（POCO属性主導では禁止）
    /// </summary>
    /// <param name="keyExpression">キー式</param>
    /// <returns>このビルダー</returns>
    /// <exception cref="NotSupportedException">POCO属性主導では物理名変更禁止</exception>
    [Obsolete("POCO属性主導型では、Fluent APIでのキー変更は禁止されています。[Key]属性を使用してください。", true)]
    public EntityModelBuilder<T> HasKey<TKey>(System.Linq.Expressions.Expression<Func<T, TKey>> keyExpression)
    {
        throw new NotSupportedException("POCO属性主導型では、Fluent APIでのキー変更は禁止されています。[Key]属性を使用してください。");
    }

    /// <summary>
    /// エンティティモデルの概要文字列を取得
    /// </summary>
    /// <returns>概要文字列</returns>
    public override string ToString()
    {
        var entityName = _entityModel.EntityType.Name;
        var topicName = _entityModel.TopicAttribute?.TopicName ?? "未定義";
        var keyCount = _entityModel.KeyProperties.Length;
        var validStatus = _entityModel.IsValid ? "有効" : "無効";

        return $"Entity: {entityName}, Topic: {topicName}, Keys: {keyCount}, Status: {validStatus}";
    }
}
