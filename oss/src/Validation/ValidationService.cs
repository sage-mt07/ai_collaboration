using KsqlDsl.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Validation;

/// <summary>
/// POCO属性主導型KafkaContextのバリデーションサービス
/// </summary>
public class ValidationService
{
    private readonly ValidationMode _mode;

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="mode">バリデーションモード</param>
    public ValidationService(ValidationMode mode = ValidationMode.Strict)
    {
        _mode = mode;
    }

    /// <summary>
    /// POCOエンティティタイプのバリデーション実行
    /// </summary>
    /// <param name="entityType">バリデーション対象のPOCOタイプ</param>
    /// <returns>バリデーション結果</returns>
    public ValidationResult ValidateEntity(Type entityType)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));

        var result = new ValidationResult { IsValid = true };

        // [Topic]属性のバリデーション
        ValidateTopicAttribute(entityType, result);

        // [Key]属性のバリデーション
        ValidateKeyAttributes(entityType, result);

        // プロパティ属性のバリデーション
        ValidatePropertyAttributes(entityType, result);

        return result;
    }

    /// <summary>
    /// [Topic]属性のバリデーション
    /// </summary>
    private void ValidateTopicAttribute(Type entityType, ValidationResult result)
    {
        var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();

        if (topicAttribute == null)
        {
            if (_mode == ValidationMode.Strict)
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスに[Topic]属性がありません。POCOとKafkaトピック名の1:1マッピングが必要です。");
            }
            else
            {
                // ゆるめモード: 自動補完
                var autoTopicName = entityType.Name;
                result.Warnings.Add($"[Topic]属性未定義のため、クラス名'{autoTopicName}'をトピック名として自動使用します（本番運用非推奨）");
                result.AutoCompletedSettings.Add($"Topic: {autoTopicName} (auto-generated)");
            }
        }
        else
        {
            // Topic属性の内容バリデーション
            if (string.IsNullOrWhiteSpace(topicAttribute.TopicName))
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスの[Topic]属性でトピック名が空です。");
            }

            if (topicAttribute.PartitionCount <= 0)
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスの[Topic]属性でPartitionCountが1以上である必要があります。現在値: {topicAttribute.PartitionCount}");
            }

            if (topicAttribute.ReplicationFactor <= 0)
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスの[Topic]属性でReplicationFactorが1以上である必要があります。現在値: {topicAttribute.ReplicationFactor}");
            }

            if (topicAttribute.RetentionMs <= 0)
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスの[Topic]属性でRetentionMsが1以上である必要があります。現在値: {topicAttribute.RetentionMs}");
            }
        }
    }

    /// <summary>
    /// [Key]属性のバリデーション
    /// </summary>
    private void ValidateKeyAttributes(Type entityType, ValidationResult result)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var keyProperties = properties.Where(p => p.GetCustomAttribute<KeyAttribute>() != null).ToArray();

        if (keyProperties.Length == 0)
        {
            if (_mode == ValidationMode.Strict)
            {
                result.IsValid = false;
                result.Errors.Add($"{entityType.Name}クラスに[Key]属性を持つプロパティがありません。Kafkaメッセージキーの定義が必要です。");
            }
            else
            {
                // ゆるめモード: 最初のプロパティを自動キーに設定
                if (properties.Length > 0)
                {
                    var firstProperty = properties[0];
                    result.Warnings.Add($"[Key]属性未定義のため、'{firstProperty.Name}'プロパティを自動的にキーとして使用します（本番運用非推奨）");
                    result.AutoCompletedSettings.Add($"Key: {firstProperty.Name} (auto-generated)");
                }
                else
                {
                    result.IsValid = false;
                    result.Errors.Add($"{entityType.Name}クラスにプロパティがないため、キーを自動設定できません。");
                }
            }
        }
        else
        {
            // 複合キーの順序重複チェック
            var orderGroups = keyProperties.GroupBy(p => p.GetCustomAttribute<KeyAttribute>()!.Order);
            foreach (var group in orderGroups)
            {
                if (group.Count() > 1)
                {
                    var propertyNames = string.Join(", ", group.Select(p => p.Name));
                    result.IsValid = false;
                    result.Errors.Add($"{entityType.Name}クラスの[Key]属性で同じOrder値({group.Key})を持つプロパティが複数あります: {propertyNames}");
                }
            }
        }
    }

    /// <summary>
    /// プロパティ属性のバリデーション
    /// </summary>
    private void ValidatePropertyAttributes(Type entityType, ValidationResult result)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // [MaxLength]属性のバリデーション
            var maxLengthAttr = property.GetCustomAttribute<MaxLengthAttribute>();
            if (maxLengthAttr != null)
            {
                if (property.PropertyType != typeof(string) && property.PropertyType != typeof(string))
                {
                    result.Warnings.Add($"{entityType.Name}.{property.Name}プロパティの[MaxLength]属性は文字列型以外では効果がありません。プロパティ型: {property.PropertyType.Name}");
                }
            }

            // [DefaultValue]属性のバリデーション
            var defaultValueAttr = property.GetCustomAttribute<DefaultValueAttribute>();
            if (defaultValueAttr != null && defaultValueAttr.Value != null)
            {
                var valueType = defaultValueAttr.Value.GetType();
                var propertyType = property.PropertyType;

                // Nullable型の場合は基底型を取得
                var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

                if (!underlyingType.IsAssignableFrom(valueType))
                {
                    result.Warnings.Add($"{entityType.Name}.{property.Name}プロパティの[DefaultValue]属性の値型({valueType.Name})がプロパティ型({propertyType.Name})と一致しません。");
                }
            }
        }
    }

    /// <summary>
    /// バリデーション結果のコンソール出力
    /// </summary>
    /// <param name="result">バリデーション結果</param>
    public static void PrintValidationResult(ValidationResult result)
    {
        if (result.IsValid && !result.HasIssues)
        {
            Console.WriteLine("✅ バリデーション成功: 全ての設定が正常です。");
            return;
        }

        if (result.Errors.Any())
        {
            Console.WriteLine("❌ バリデーションエラー:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
        }

        if (result.Warnings.Any())
        {
            Console.WriteLine("⚠️  バリデーション警告:");
            foreach (var warning in result.Warnings)
            {
                Console.WriteLine($"  - {warning}");
            }
        }

        if (result.AutoCompletedSettings.Any())
        {
            Console.WriteLine("🔧 自動補完設定:");
            foreach (var setting in result.AutoCompletedSettings)
            {
                Console.WriteLine($"  - {setting}");
            }
        }
    }
}
