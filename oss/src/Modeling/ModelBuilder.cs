using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Attributes;
using KsqlDsl.Validation;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace KsqlDsl.Modeling;

public class ModelBuilder
{
    private readonly Dictionary<Type, EntityModel> _entityModels = new();
    private readonly ValidationService _validationService;
    private AvroSchemaRegistrationService? _schemaRegistrationService;

    public ModelBuilder(ValidationMode validationMode = ValidationMode.Strict)
    {
        _validationService = new ValidationService(validationMode);
    }

    public void SetSchemaRegistrationService(AvroSchemaRegistrationService? schemaRegistrationService)
    {
        _schemaRegistrationService = schemaRegistrationService;
    }

    public EntityModelBuilder<T> Event<T>() where T : class
    {
        var entityType = typeof(T);

        if (_entityModels.ContainsKey(entityType))
        {
            throw new InvalidOperationException($"エンティティ {entityType.Name} は既に登録済みです。同じエンティティの重複登録はできません。");
        }

        var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();
        var allProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var keyProperties = Array.FindAll(allProperties, p => p.GetCustomAttribute<KeyAttribute>() != null);

        Array.Sort(keyProperties, (p1, p2) =>
        {
            var order1 = p1.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
            var order2 = p2.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
            return order1.CompareTo(order2);
        });

        var validationResult = _validationService.ValidateEntity(entityType);

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

    public Dictionary<Type, EntityModel> GetEntityModels()
    {
        return new Dictionary<Type, EntityModel>(_entityModels);
    }

    public EntityModel? GetEntityModel(Type entityType)
    {
        return _entityModels.TryGetValue(entityType, out var model) ? model : null;
    }

    public EntityModel? GetEntityModel<T>() where T : class
    {
        return GetEntityModel(typeof(T));
    }

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

    public async Task BuildAsync()
    {
        var validationResult = ValidateAllEntities();

        if (!validationResult.IsValid)
        {
            var errorMessage = "モデル構築に失敗しました。以下のエラーを解決してください:" + Environment.NewLine;
            errorMessage += string.Join(Environment.NewLine, validationResult.Errors);
            throw new InvalidOperationException(errorMessage);
        }

        if (validationResult.Warnings.Count > 0 || validationResult.AutoCompletedSettings.Count > 0)
            ValidationService.PrintValidationResult(validationResult);

        if (_schemaRegistrationService != null)
        {
            try
            {
                await _schemaRegistrationService.RegisterAllSchemasAsync(_entityModels);
            }
            catch (Exception ex)
            {
                if (_validationService.GetValidationMode() == ValidationMode.Strict)
                    throw new InvalidOperationException($"Avroスキーマ自動登録に失敗しました: {ex.Message}", ex);
                else
                    Console.WriteLine($"[WARNING] Avroスキーマ登録に失敗しましたが、続行します: {ex.Message}");
            }
        }
    }

    public void Build()
    {
        var validationResult = ValidateAllEntities();

        if (!validationResult.IsValid)
        {
            var errorMessage = "モデル構築に失敗しました。以下のエラーを解決してください:" + Environment.NewLine;
            errorMessage += string.Join(Environment.NewLine, validationResult.Errors);
            throw new InvalidOperationException(errorMessage);
        }

        if (validationResult.Warnings.Count > 0 || validationResult.AutoCompletedSettings.Count > 0)
            ValidationService.PrintValidationResult(validationResult);

        if (_schemaRegistrationService != null)
            Console.WriteLine("[INFO] Avroスキーマ登録は非同期で実行されます。BuildAsync()を使用することを推奨します。");
    }

    public async Task<List<string>> GetRegisteredSchemasAsync()
    {
        if (_schemaRegistrationService == null)
            return new List<string>();
        return await _schemaRegistrationService.GetRegisteredSchemasAsync();
    }

    public async Task<bool> CheckEntitySchemaCompatibilityAsync<T>() where T : class
    {
        if (_schemaRegistrationService == null)
            return false;

        var entityType = typeof(T);
        var entityModel = GetEntityModel<T>();

        if (entityModel == null)
            return false;

        var topicName = entityModel.TopicAttribute?.TopicName ?? entityType.Name;
        var valueSchema = SchemaGenerator.GenerateSchema(entityType);

        return await _schemaRegistrationService.CheckSchemaCompatibilityAsync($"{topicName}-value", valueSchema);
    }

    private void ValidateRequiredProperties<T>(Type entityType, ValidationResult validationResult) where T : class
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var schemaProperties = SchemaGenerator.GetGenerationStats(entityType);

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

        foreach (var property in properties)
        {
            if (property.GetCustomAttribute<KsqlDsl.Modeling.KafkaIgnoreAttribute>() != null)
                continue;

            if (IsNullableProperty(property))
            {
                var defaultValueAttr = property.GetCustomAttribute<DefaultValueAttribute>();
                if (defaultValueAttr?.Value != null)
                {
                    validationResult.Warnings.Add($"{entityType.Name}.{property.Name}はNullable型ですが、デフォルト値が設定されています。");
                }
            }
        }
    }

    private void ValidateSchemaCompatibility<T>(Type entityType, ValidationResult validationResult) where T : class
    {
        var keyProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<KeyAttribute>() != null).ToArray();

        if (keyProperties.Length > 1)
        {
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

    private bool IsNullableProperty(PropertyInfo property)
    {
        var propertyType = property.PropertyType;

        if (Nullable.GetUnderlyingType(propertyType) != null)
            return true;

        if (propertyType.IsValueType)
            return false;

        try
        {
            var nullabilityContext = new NullabilityInfoContext();
            var nullabilityInfo = nullabilityContext.Create(property);
            return nullabilityInfo.WriteState == NullabilityState.Nullable ||
                   nullabilityInfo.ReadState == NullabilityState.Nullable;
        }
        catch
        {
            return !propertyType.IsValueType;
        }
    }

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
}