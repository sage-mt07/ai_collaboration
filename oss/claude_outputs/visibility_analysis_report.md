# KsqlDsl可視性分析レポート - Public → Internal変換候補

## 📋 分析概要

**対象コードベース**: KsqlDsl（全152ファイル）  
**分析対象**: publicクラス・メソッド・プロパティ  
**目的**: 過剰なpublic宣言の特定とinternal化推奨

---

## 🎯 変換候補一覧（高優先度）

### 1. Core層内部実装クラス

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Core/Models/KeyExtractor.cs | 13 | `KeyExtractor` | public static | internal static | Core層内部ユーティリティ |
| Core/Models/ProducerKey.cs | 6 | `ProducerKey` | public | internal | 内部キー管理用 |
| Core/Configuration/CoreSettings.cs | 5 | `CoreSettings` | public | internal | Core層設定、外部不要 |
| Core/Configuration/CoreSettingsProvider.cs | 7 | `CoreSettingsProvider` | public | internal | DI内部実装 |
| Core/Configuration/CoreSettingsChangedEventArgs.cs | 5 | `CoreSettingsChangedEventArgs` | public | internal | 内部イベント引数 |

### 2. Query層Builder実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Query/Builders/GroupByBuilder.cs | 11 | `GroupByBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/HavingBuilder.cs | 11 | `HavingBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/JoinBuilder.cs | 11 | `JoinBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/ProjectionBuilder.cs | 11 | `ProjectionBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/SelectBuilder.cs | 11 | `SelectBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/WindowBuilder.cs | 11 | `WindowBuilder` | public | internal | 内部Builder実装 |

### 3. Serialization内部管理

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Serialization/Avro/Core/AvroSerializerFactory.cs | 11 | `AvroSerializerFactory` | public | internal | 内部Factory |
| Serialization/Avro/Cache/AvroSerializerCache.cs | 13 | `AvroSerializerCache` | public | internal | キャッシュ実装 |
| Serialization/Avro/Management/AvroSchemaBuilder.cs | 11 | `AvroSchemaBuilder` | public | internal | 内部スキーマ生成 |
| Serialization/Avro/Management/AvroSchemaRepository.cs | 9 | `AvroSchemaRepository` | public | internal | 内部Repository |

### 4. Messaging内部実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Messaging/Consumers/Core/KafkaConsumer.cs | 15 | `KafkaConsumer<TValue, TKey>` | public | internal | Manager経由で使用 |
| Messaging/Producers/Core/KafkaProducer.cs | 15 | `KafkaProducer<T>` | public | internal | Manager経由で使用 |
| Messaging/Core/PoolMetrics.cs | 9 | `PoolMetrics` | public | internal | 内部メトリクス |

---

## 🔄 修正サンプル（Before/After）

### Core/Models/KeyExtractor.cs
```csharp
// Before
public static class KeyExtractor
{
    public static bool IsCompositeKey(EntityModel entityModel) { ... }
    public static Type DetermineKeyType(EntityModel entityModel) { ... }
}

// After  
internal static class KeyExtractor
{
    internal static bool IsCompositeKey(EntityModel entityModel) { ... }
    internal static Type DetermineKeyType(EntityModel entityModel) { ... }
}
```

### Query/Builders/GroupByBuilder.cs
```csharp
// Before
public class GroupByBuilder : IKsqlBuilder
{
    public KsqlBuilderType BuilderType => KsqlBuilderType.GroupBy;
    public string Build(Expression expression) { ... }
}

// After
internal class GroupByBuilder : IKsqlBuilder
{
    public KsqlBuilderType BuilderType => KsqlBuilderType.GroupBy;
    public string Build(Expression expression) { ... }
}
```

---

## ✅ Public維持推奨（理由付き）

### Application層 - ユーザーAPI
```csharp
// これらは外部公開必須のため維持
public abstract class KafkaContext : KafkaContextCore
public class KsqlContextBuilder  
public class KsqlContextOptions
public static class AvroSchemaInfoExtensions
```
**理由**: ユーザーが直接使用するAPI群

### Core/Abstractions - 契約定義
```csharp
// インターフェース群は維持
public interface IKafkaContext
public interface IEntitySet<T>
public interface ISerializationManager<T>
```
**理由**: 外部実装・テスト・拡張に必要

### 属性・例外クラス
```csharp
// 属性とPublic例外は維持
public class TopicAttribute : Attribute
public class KeyAttribute : Attribute  
public class KafkaIgnoreAttribute : Attribute
public class ValidationResult
```
**理由**: ユーザーコードでの直接使用

---

## 🛠️ 実装ガイドライン

### InternalsVisibleTo設定
既存の`AssemblyInfo.cs`を拡張：
```csharp
[assembly: InternalsVisibleTo("KsqlDslTests")]
[assembly: InternalsVisibleTo("KsqlDsl.Tests.Integration")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")] // Moq対応
```

### 段階的移行戦略
1. **Phase 1**: Core層内部クラス（影響小）
2. **Phase 2**: Query/Serialization Builder群  
3. **Phase 3**: Messaging内部実装
4. **Phase 4**: 完全性検証・テスト

---

## ⚠️ 注意点・設計指摘

### 1. Application層の統合クラス
`KsqlContext.cs`（3番ファイル）の`EventSetWithSimplifiedServices<T>`は**internal**が適切
```csharp
// 現在: publicで宣言されているが外部使用なし
internal class EventSetWithSimplifiedServices<T> : EventSet<T>
```

### 2. Builder Pattern設計
Query/Builders群は全てIKsqlBuilderを実装しているが、直接インスタンス化は不要
→ Factory経由アクセスにしてinternal化推奨

### 3. Exception階層
一部Exception（SchemaRegistrationFatalException等）は**internal**が妥当
運用例外は内部詳細のため

---

## 📊 可視性設計ベストプラクティス

### 1. レイヤー別可視性原則
- **Application層**: Public（ユーザーAPI）
- **Core/Abstractions**: Public（契約）  
- **Core/実装**: Internal（詳細実装）
- **Infrastructure**: Internal（技術詳細）

### 2. AI自動生成対応
```csharp
// 生成ルール例
// ユーザーAPI = public
// 内部実装 = internal  
// Builder/Factory = internal（DI経由）
// Exception = public（ユーザー処理用）/internal（内部詳細）
```

### 3. 今後の指針
- 新規クラス作成時は**internal first**
- 外部使用が明確な場合のみpublic昇格
- 定期的な可視性レビュー実施

---

## 🎯 変換優先度マトリクス

| 優先度 | 対象 | 影響度 | 実装難易度 |
|--------|------|--------|------------|
| 🔥 高 | Core内部クラス | 低 | 易 |
| 🔥 高 | Builder実装群 | 低 | 易 |  
| 🟡 中 | Serialization管理 | 中 | 中 |
| 🟡 中 | Messaging実装 | 中 | 中 |
| 🔵 低 | Exception詳細 | 低 | 易 |

**総計**: 約30-40個のpublicクラス/メソッドがinternal化候補
**期待効果**: API表面積20-30%削減、設計意図明確化