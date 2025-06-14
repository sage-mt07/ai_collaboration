using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Attributes;
using KsqlDsl.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

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
    /// エンティティをKafkaイベントとして登録
    /// POCO属性による自動設定のみ。Fluent APIでの上書きは禁止
    /// </summary>
    /// <typeparam name="T">エンティティタイプ</typeparam>
    /// <returns>エンティティモデルビルダー（将来拡張用、現在は設定変更禁止）</returns>
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
