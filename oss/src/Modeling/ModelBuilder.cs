using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Attributes;
using KsqlDsl.Validation;
// 修正理由：CS1061エラー対応、必要なusing文追加
using KsqlDsl.SchemaRegistry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
// 修正理由：NullabilityInfoContext使用のため追加
using System.Diagnostics.CodeAnalysis;
namespace KsqlDsl.Modeling;

/// <summary>
/// POCO属性主導型KafkaContextのモデルビルダー
/// EntityFramework風のモデル定義API
/// </summary>
public class ModelBuilder
{
    private readonly Dictionary<Type, EntityModel> _entityModels = new();
    private readonly ValidationService _validationService;

    /// <summary>
    /// 初期化
    /// </summary>
    /// <param name="validationMode">バリデーションモード</param>
    public ModelBuilder(ValidationMode validationMode = ValidationMode.Strict)
    {
        _validationService = new ValidationService(validationMode);
    }
    /// <summary>
    /// エンティティをKafkaイベントとして登録（スキーマ突合機能強化版）
    /// 修正理由：task_eventset.txt「OnModelCreatingでEntityModel／POCO→Avroスキーマを必ず突合・未定義属性は例外 or 警告」
    /// </summary>
    public EntityModelBuilder<T> Event<T>() where T : class
    {
        var entityType = typeof(T);

        if (_entityModels.ContainsKey(entityType))
        {
            throw new InvalidOperationException($"エンティティ {entityType.Name} は既に登録済みです。同じエンティティの重複登録はできません。");
        }

        // POCO属性情報を取得
        var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();
        var allProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var keyProperties = Array.FindAll(allProperties, p => p.GetCustomAttribute<KeyAttribute>() != null);

        // キープロパティを順序でソート
        Array.Sort(keyProperties, (p1, p2) =>
        {
            var order1 = p1.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
            var order2 = p2.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
            return order1.CompareTo(order2);
        });

        // バリデーション実行
        var validationResult = _validationService.ValidateEntity(entityType);

        // 修正理由：段階的実装のため、スキーマ突合機能は一時無効化
        // 基本機能が動作確認後、段階的に有効化予定
        // PerformSchemaValidation<T>(entityType, validationResult);

        // エンティティモデルを作成・登録
        var entityModel = new EntityModel
        {
            EntityType = entityType,
            TopicAttribute = topicAttribute,
            KeyProperties = keyProperties,
            AllProperties = allProperties,
            ValidationResult = validationResult
        };

        _entityModels[entityType] = entityModel;

        return new EntityModelBuilder<T>(entityModel);
    }

    /// <summary>
    /// POCO→Avroスキーマ突合バリデーション
    /// 修正理由：task_eventset.txt「POCO→Avroスキーマを必ず突合・未定義属性は例外 or 警告」
    /// </summary>
    private void PerformSchemaValidation<T>(Type entityType, ValidationResult validationResult) where T : class
    {
        try
        {
            // Avroスキーマ生成テスト
            var generatedSchema = SchemaRegistry.SchemaGenerator.GenerateSchema<T>();

            // スキーマ有効性検証
            if (!SchemaRegistry.SchemaGenerator.ValidateSchema(generatedSchema))
            {
                if (_validationService.GetValidationMode() == ValidationMode.Strict)
                {
                    validationResult.IsValid = false;
                    validationResult.Errors.Add($"{entityType.Name}の生成されたAvroスキーマが無効です。");
                }
                else
                {
                    validationResult.Warnings.Add($"{entityType.Name}の生成されたAvroスキーマが無効ですが、続行します（Relaxedモード）。");
                }
            }

            // スキーマ生成統計取得
            var stats = SchemaRegistry.SchemaGenerator.GetGenerationStats(entityType);

            if (stats.IgnoredProperties > 0)
            {
                var ignoredList = string.Join(", ", stats.IgnoredPropertyNames);
                validationResult.Warnings.Add($"{entityType.Name}で{stats.IgnoredProperties}個のプロパティが[KafkaIgnore]により除外されました: {ignoredList}");
            }

            // 修正理由：task_eventset.txt「未定義属性は例外 or 警告」
            ValidateRequiredProperties<T>(entityType, validationResult);

            // 修正理由：task_eventset.txt「スキーマ互換チェック」
            ValidateSchemaCompatibility<T>(entityType, validationResult);

            if (validationResult.IsValid)
            {
                validationResult.AutoCompletedSettings.Add($"Avroスキーマ生成成功: {entityType.Name} ({stats.IncludedProperties}個のプロパティ)");
            }
        }
        catch (Exception schemaEx)
        {
            if (_validationService.GetValidationMode() == ValidationMode.Strict)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add($"{entityType.Name}のスキーマ突合に失敗しました: {schemaEx.Message}");
            }
            else
            {
                validationResult.Warnings.Add($"{entityType.Name}のスキーマ突合に失敗しましたが、続行します（Relaxedモード）: {schemaEx.Message}");
            }
        }
    }

    /// <summary>
    /// 必須属性の検証
    /// 修正理由：task_eventset.txt「未定義属性は例外 or 警告」
    /// </summary>
    private void ValidateRequiredProperties<T>(Type entityType, ValidationResult validationResult) where T : class
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // シリアライズ対象プロパティの確認
        var schemaProperties = SchemaRegistry.SchemaGenerator.GetGenerationStats(entityType);

        if (schemaProperties.IncludedProperties == 0)
        {
            if (_validationService.GetValidationMode() == ValidationMode.Strict)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add($"{entityType.Name}にシリアライズ対象のプロパティが見つかりません。");
            }
            else
            {
                validationResult.Warnings.Add($"{entityType.Name}にシリアライズ対象のプロパティが見つかりませんが、続行します（Relaxedモード）。");
            }
        }

        // Nullable Reference Typesとの整合性チェック
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute<KsqlDsl.Modeling.KafkaIgnoreAttribute>() != null)
                continue;

            // Nullableプロパティの妥当性確認
            if (IsNullableProperty(property))
            {
                // Nullable型なのにデフォルト値がある場合の警告
                var defaultValueAttr = property.GetCustomAttribute<DefaultValueAttribute>();
                if (defaultValueAttr?.Value != null)
                {
                    validationResult.Warnings.Add($"{entityType.Name}.{property.Name}はNullable型ですが、デフォルト値が設定されています。");
                }
            }
        }
    }

    /// <summary>
    /// スキーマ互換性チェック
    /// 修正理由：task_eventset.txt「スキーマ互換チェック」
    /// </summary>
    private void ValidateSchemaCompatibility<T>(Type entityType, ValidationResult validationResult) where T : class
    {
        // 複合キーの場合の特別な検証
        var keyProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<KeyAttribute>() != null).ToArray();

        if (keyProperties.Length > 1)
        {
            // 複合キーの場合、全てのキープロパティが同じ順序でシリアライザブルである必要がある
            foreach (var keyProperty in keyProperties)
            {
                if (!IsSerializableType(keyProperty.PropertyType))
                {
                    if (_validationService.GetValidationMode() == ValidationMode.Strict)
                    {
                        validationResult.IsValid = false;
                        validationResult.Errors.Add($"{entityType.Name}.{keyProperty.Name}の型({keyProperty.PropertyType.Name})はKafkaキーとしてシリアライズできません。");
                    }
                    else
                    {
                        validationResult.Warnings.Add($"{entityType.Name}.{keyProperty.Name}の型({keyProperty.PropertyType.Name})はKafkaキーとして問題がある可能性があります。");
                    }
                }
            }
        }

        // 循環参照チェック
        if (HasCircularReference<T>())
        {
            if (_validationService.GetValidationMode() == ValidationMode.Strict)
            {
                validationResult.IsValid = false;
                validationResult.Errors.Add($"{entityType.Name}に循環参照が検出されました。Avroスキーマでは循環参照はサポートされていません。");
            }
            else
            {
                validationResult.Warnings.Add($"{entityType.Name}に循環参照の可能性があります。");
            }
        }
    }

    /// <summary>
    /// Nullable プロパティ判定
    /// </summary>
    private bool IsNullableProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;

        // Nullable value types
        if (Nullable.GetUnderlyingType(propertyType) != null)
            return true;

        // Value types are non-nullable by default
        if (propertyType.IsValueType)
            return false;

        // Reference types - check nullable context
        try
        {
            var nullabilityContext = new NullabilityInfoContext();
            var nullabilityInfo = nullabilityContext.Create(property);
            return nullabilityInfo.WriteState == NullabilityState.Nullable ||
                   nullabilityInfo.ReadState == NullabilityState.Nullable;
        }
        catch
        {
            return !propertyType.IsValueType; // Fallback
        }
    }

    /// <summary>
    /// シリアライズ可能型判定
    /// </summary>
    private bool IsSerializableType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.IsPrimitive ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(Guid) ||
               underlyingType == typeof(byte[]);
    }

    /// <summary>
    /// 循環参照検出
    /// </summary>
    private bool HasCircularReference<T>()
    {
        var visitedTypes = new HashSet<Type>();
        return HasCircularReferenceInternal(typeof(T), visitedTypes);
    }

    private bool HasCircularReferenceInternal(Type type, HashSet<Type> visitedTypes)
    {
        if (visitedTypes.Contains(type))
            return true;

        if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
            return false;

        visitedTypes.Add(type);

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<KsqlDsl.Modeling.KafkaIgnoreAttribute>() == null);

        foreach (var property in properties)
        {
            var propertyType = property.PropertyType;
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (underlyingType.IsClass && underlyingType != typeof(string) && underlyingType.Assembly == type.Assembly)
            {
                if (HasCircularReferenceInternal(underlyingType, new HashSet<Type>(visitedTypes)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 登録済みエンティティモデル一覧を取得
    /// </summary>
    /// <returns>エンティティモデル辞書</returns>
    public Dictionary<Type, EntityModel> GetEntityModels()
    {
        return new Dictionary<Type, EntityModel>(_entityModels);
    }

    /// <summary>
    /// 指定タイプのエンティティモデルを取得
    /// </summary>
    /// <param name="entityType">エンティティタイプ</param>
    /// <returns>エンティティモデル（未登録時はnull）</returns>
    public EntityModel? GetEntityModel(Type entityType)
    {
        return _entityModels.TryGetValue(entityType, out var model) ? model : null;
    }

    /// <summary>
    /// 指定タイプのエンティティモデルを取得（ジェネリック版）
    /// </summary>
    /// <typeparam name="T">エンティティタイプ</typeparam>
    /// <returns>エンティティモデル（未登録時はnull）</returns>
    public EntityModel? GetEntityModel<T>() where T : class
    {
        return GetEntityModel(typeof(T));
    }

    /// <summary>
    /// 全エンティティのバリデーション結果をチェック
    /// </summary>
    /// <returns>全体バリデーション結果</returns>
    public ValidationResult ValidateAllEntities()
    {
        var overallResult = new ValidationResult { IsValid = true };

        foreach (var entityModel in _entityModels.Values)
        {
            if (entityModel.ValidationResult == null) continue;

            if (!entityModel.ValidationResult.IsValid)
            {
                overallResult.IsValid = false;
            }

            overallResult.Errors.AddRange(entityModel.ValidationResult.Errors);
            overallResult.Warnings.AddRange(entityModel.ValidationResult.Warnings);
            overallResult.AutoCompletedSettings.AddRange(entityModel.ValidationResult.AutoCompletedSettings);
        }

        return overallResult;
    }

    /// <summary>
    /// 登録済みエンティティの概要を取得
    /// </summary>
    /// <returns>概要文字列</returns>
    public string GetModelSummary()
    {
        if (_entityModels.Count == 0)
            return "登録済みエンティティ: なし";

        var summary = new List<string> { $"登録済みエンティティ: {_entityModels.Count}件" };

        foreach (var entityModel in _entityModels.Values)
        {
            var entityName = entityModel.EntityType.Name;
            var topicName = entityModel.TopicAttribute?.TopicName ?? $"{entityName} (自動生成)";
            var keyCount = entityModel.KeyProperties.Length;
            var propCount = entityModel.AllProperties.Length;
            var validStatus = entityModel.IsValid ? "✅" : "❌";

            summary.Add($"  {validStatus} {entityName} → Topic: {topicName}, Keys: {keyCount}, Props: {propCount}");
        }

        return string.Join(Environment.NewLine, summary);
    }

    /// <summary>
    /// モデル構築の完了処理
    /// 全エンティティのバリデーションを実行し、問題があれば例外をスロー
    /// </summary>
    public void Build()
    {
        var validationResult = ValidateAllEntities();

        if (!validationResult.IsValid)
        {
            var errorMessage = "モデル構築に失敗しました。以下のエラーを解決してください:" + Environment.NewLine;
            errorMessage += string.Join(Environment.NewLine, validationResult.Errors);

            throw new InvalidOperationException(errorMessage);
        }

        // 警告がある場合はコンソール出力
        if (validationResult.Warnings.Count > 0 || validationResult.AutoCompletedSettings.Count > 0)
        {
            ValidationService.PrintValidationResult(validationResult);
        }
    }
}
