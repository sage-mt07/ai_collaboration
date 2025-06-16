using System;

namespace KsqlDsl.Modeling;

public class EntityModelBuilder<T> where T : class
{
    private readonly EntityModel _entityModel;

    internal EntityModelBuilder(EntityModel entityModel)
    {
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
    }

    public EntityModel GetModel()
    {
        return _entityModel;
    }

    [Obsolete("POCO属性主導型では、Fluent APIでのトピック名変更は禁止されています。[Topic]属性を使用してください。", true)]
    public EntityModelBuilder<T> HasTopicName(string topicName)
    {
        throw new NotSupportedException("POCO属性主導型では、Fluent APIでのトピック名変更は禁止されています。[Topic]属性を使用してください。");
    }

    [Obsolete("POCO属性主導型では、Fluent APIでのキー変更は禁止されています。[Key]属性を使用してください。", true)]
    public EntityModelBuilder<T> HasKey<TKey>(System.Linq.Expressions.Expression<Func<T, TKey>> keyExpression)
    {
        throw new NotSupportedException("POCO属性主導型では、Fluent APIでのキー変更は禁止されています。[Key]属性を使用してください。");
    }

    public override string ToString()
    {
        var entityName = _entityModel.EntityType.Name;
        var topicName = _entityModel.TopicAttribute?.TopicName ?? "未定義";
        var keyCount = _entityModel.KeyProperties.Length;
        var validStatus = _entityModel.IsValid ? "有効" : "無効";

        return $"Entity: {entityName}, Topic: {topicName}, Keys: {keyCount}, Status: {validStatus}";
    }
}
