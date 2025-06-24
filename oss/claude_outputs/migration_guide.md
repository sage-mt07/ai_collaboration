# Phase 2: 重複メソッド統一 - 移行ガイド

## 削除されたクラス・メソッドと代替手段

### 1. AvroModelBuilder 系削除 → UnifiedSchemaGenerator へ統合

#### 削除されたメソッド:
```csharp
❌ AvroModelBuilder.Entity<T>()
❌ AvroEntityTypeBuilder<T>.ToTopic() 
❌ AvroEntityTypeBuilder<T>.HasKey()
❌ AvroPropertyBuilder<T>.IsRequired()
```

#### 代替手段:
```csharp
✅ UnifiedSchemaGenerator.GenerateSchema<T>()           // Entity<T>() の代替
✅ AvroEntityConfiguration.TopicName プロパティ        // ToTopic() の代替
✅ AvroEntityConfiguration.KeyProperties プロパティ    // HasKey() の代替  
✅ [Required] 属性使用                                 // IsRequired() の代替
```

#### 移行例:
```csharp
// ❌ 削除前
var builder = new AvroModelBuilder();
builder.Entity<MyEntity>()
    .ToTopic("my-topic")
    .HasKey(x => x.Id);

// ✅ 移行後
var config = new AvroEntityConfiguration(typeof(MyEntity))
{
    TopicName = "my-topic",
    KeyProperties = new[] { typeof(MyEntity).GetProperty(nameof(MyEntity.Id)) }
};
var schema = UnifiedSchemaGenerator.GenerateSchema(config);
```

### 2. ValidationResult 重複削除 → Core.Abstractions へ統一

#### 削除されたクラス:
```csharp
❌ Core.Validation.CoreValidationResult 
```

#### 代替手段:
```csharp
✅ Core.Abstractions.ValidationResult  // メイン定義を使用
```

#### 移行例:
```csharp
// ❌ 削除前
using KsqlDsl.Core.Validation;
var result = new CoreValidationResult();

// ✅ 移行後
using KsqlDsl.Core.Abstractions;
var result = new ValidationResult();
```

## 削除対象メソッド一覧 (30個)

### Builder系 (20個)
- `AvroModelBuilder.Entity<T>()`
- `AvroModelBuilder.HasEntity<T>()`
- `AvroModelBuilder.HasEntity(Type)`
- `AvroModelBuilder.RemoveEntity<T>()`
- `AvroModelBuilder.Clear()`
- `AvroModelBuilder.Build()`
- `AvroModelBuilder.EntityCount`
- `AvroModelBuilder.GetEntityConfiguration<T>()`
- `AvroModelBuilder.GetEntityConfiguration(Type)`
- `AvroModelBuilder.GetModelSummary()`
- `AvroModelBuilder.ValidateModel()`
- `AvroModelBuilder.ApplyAttributeBasedConfiguration()`
- `AvroModelBuilder.ApplyConvention()`
- `AvroEntityTypeBuilder<T>.ToTopic()`
- `AvroEntityTypeBuilder<T>.HasKey<TKey>()`
- `AvroEntityTypeBuilder<T>.WithPartitions()`
- `AvroEntityTypeBuilder<T>.WithReplicationFactor()`
- `AvroEntityTypeBuilder<T>.ValidateOnStartup()`
- `AvroEntityTypeBuilder<T>.EnableCaching()`
- `AvroEntityTypeBuilder<T>.AsStream()`

### PropertyBuilder系 (5個)
- `AvroPropertyBuilder<T, TProperty>.IsRequired()`
- `AvroPropertyBuilder<T, TProperty>.HasMaxLength()`
- `AvroPropertyBuilder<T, TProperty>.HasPrecision()`
- `AvroPropertyBuilder<T, TProperty>.HasDefaultValue()`
- `AvroPropertyBuilder<T, TProperty>.IsIgnored()`

### ValidationResult重複 (5個)
- `CoreValidationResult.IsValid`
- `CoreValidationResult.Errors`
- `CoreValidationResult.Warnings`
- `CoreValidationResult` コンストラクタ
- `CoreValidationResult` 関連メソッド

## 影響を受けるファイル

### 修正が必要:
- `Core/CoreLayerValidation.cs` ✅ 修正済み
- その他の `CoreValidationResult` 参照箇所

### 自動対応:
- `UnifiedSchemaGenerator` は既存実装で全機能カバー済み
- `AvroEntityConfiguration` は既存実装で Builder 機能カバー済み

## 次のステップ

Phase 2 完了後、Phase 3 (段階的削除) に進む:
- レガシー互換メソッド (~15個)
- 実験的機能の判定 (~10個)
