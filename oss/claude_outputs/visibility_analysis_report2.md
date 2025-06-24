# KsqlDsl可視性分析レポート - Public → Internal変換候補

## 📋 分析概要

**対象コードベース**: KsqlDsl（全152ファイル）  
**分析対象**: publicクラス・メソッド・プロパティ  
**目的**: 過剰なpublic宣言の特定とinternal化推奨

---

## 🎯 変換候補一覧（優先度別完全版）

### 1. 🔥 最高優先度 - Core層基盤クラス

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Core/Models/KeyExtractor.cs | 13 | `KeyExtractor` | public static | internal static | Core層内部ユーティリティ |
| Core/Models/ProducerKey.cs | 6 | `ProducerKey` | public | internal | 内部キー管理用 |
| Core/Configuration/CoreSettings.cs | 5 | `CoreSettings` | public | internal | Core層設定、外部不要 |
| Core/Configuration/CoreSettingsProvider.cs | 7 | `CoreSettingsProvider` | public | internal | DI内部実装 |
| Core/Configuration/CoreSettingsChangedEventArgs.cs | 5 | `CoreSettingsChangedEventArgs` | public | internal | 内部イベント引数 |
| Core/Configuration/Abstractions/TopicOverrideService.cs | 7 | `TopicOverrideService` | public | internal | 内部トピック管理 |
| Core/CoreDependencyConfiguration.cs | 14 | `CoreDependencyConfiguration` | public static | internal static | DI設定内部実装 |
| Core/CoreLayerPhase3Marker.cs | 8 | `CoreLayerPhase3Marker` | public static | internal static | 内部バージョン管理 |
| Core/CoreLayerValidation.cs | 14 | `CoreLayerValidation` | public static | internal static | 内部検証ユーティリティ |

### 2. 🔥 最高優先度 - Query層実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Query/Builders/GroupByBuilder.cs | 11 | `GroupByBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/HavingBuilder.cs | 11 | `HavingBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/JoinBuilder.cs | 11 | `JoinBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/ProjectionBuilder.cs | 11 | `ProjectionBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/SelectBuilder.cs | 11 | `SelectBuilder` | public | internal | 内部Builder実装 |
| Query/Builders/WindowBuilder.cs | 11 | `WindowBuilder` | public | internal | 内部Builder実装 |
| Query/Pipeline/DMLQueryGenerator.cs | 27 | `DMLQueryGenerator` | public | internal | Query内部実装 |
| Query/Pipeline/QueryDiagnostics.cs | 15 | `QueryDiagnostics` | public | internal | 内部診断機能 |
| Query/Pipeline/QueryExecutionResult.cs | 8 | `QueryExecutionResult` | public | internal | 内部実行結果 |
| Query/Pipeline/QueryExecutionMode.cs | 8 | `QueryExecutionMode` | public enum | internal enum | 内部実行モード |
| Query/Pipeline/DerivedObjectType.cs | 8 | `DerivedObjectType` | public enum | internal enum | 内部オブジェクト型 |

### 3. 🔥 最高優先度 - REST API内部実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Query/Ksql/KsqlDbRestApiClient.cs | 157 | `KsqlQueryRequest` | public | internal | REST API内部リクエスト |
| Query/Ksql/KsqlDbRestApiClient.cs | 163 | `KsqlStatementRequest` | public | internal | REST API内部リクエスト |
| Query/Ksql/KsqlDbRestApiClient.cs | 167 | `KsqlQueryResponse` | public | internal | REST API内部レスポンス |
| Query/Ksql/KsqlDbRestApiClient.cs | 172 | `KsqlStatementResponse` | public | internal | REST API内部レスポンス |
| Query/Ksql/KsqlDbRestApiClient.cs | 177 | `KsqlDbException` | public | internal | 内部例外クラス |

### 4. 🟡 高優先度 - Serialization内部実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Serialization/Avro/Core/AvroSerializerFactory.cs | 11 | `AvroSerializerFactory` | public | internal | 内部Factory |
| Serialization/Avro/Cache/AvroSerializerCache.cs | 13 | `AvroSerializerCache` | public | internal | キャッシュ実装 |
| Serialization/Avro/Management/AvroSchemaBuilder.cs | 11 | `AvroSchemaBuilder` | public | internal | 内部スキーマ生成 |
| Serialization/Avro/Management/AvroSchemaRepository.cs | 9 | `AvroSchemaRepository` | public | internal | 内部Repository |
| Serialization/Avro/Core/AvroSchema.cs | 8 | `AvroSchema` | public | internal | 内部スキーマ表現 |
| Serialization/Avro/Core/AvroField.cs | 4 | `AvroField` | public | internal | 内部フィールド表現 |
| Serialization/Avro/Core/AvroSchemaInfo.cs | 8 | `AvroSchemaInfo` | public | internal | 内部スキーマ情報 |
| Serialization/Avro/Core/UnifiedSchemaGenerator.cs | 11 | `UnifiedSchemaGenerator` | public static | internal static | 内部スキーマ生成器 |

### 5. 🟡 高優先度 - Messaging内部実装

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Messaging/Consumers/Core/KafkaConsumer.cs | 15 | `KafkaConsumer<TValue, TKey>` | public | internal | Manager経由で使用 |
| Messaging/Producers/Core/KafkaProducer.cs | 15 | `KafkaProducer<T>` | public | internal | Manager経由で使用 |
| Messaging/Core/PoolMetrics.cs | 9 | `PoolMetrics` | public | internal | 内部メトリクス |
| Messaging/Consumers/Core/ConsumerInstance.cs | 8 | `ConsumerInstance` | public | internal | プール内部管理 |
| Messaging/Consumers/Core/PooledConsumer.cs | 10 | `PooledConsumer` | public | internal | プール内部管理 |
| Messaging/Producers/Core/PooledProducer.cs | 11 | `PooledProducer` | public | internal | プール内部管理 |

### 6. 🟡 高優先度 - Manager系（慎重検討要）

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Messaging/Producers/KafkaProducerManager.cs | 15 | `KafkaProducerManager` | public | internal | DI経由使用前提 |
| Messaging/Consumers/KafkaConsumerManager.cs | 15 | `KafkaConsumerManager` | public | internal | DI経由使用前提 |

### 7. 🔵 中優先度 - Configuration系

| ファイル | 行番号 | シンボル | 現在 | 推奨 | 理由 |
|---------|--------|---------|------|------|------|
| Configuration/Abstractions/SchemaGenerationStats.cs | 11 | `GetSummary()` | public | internal | デバッグ用メソッド |
| Configuration/Abstractions/SchemaGenerationOptions.cs | 25 | `Clone()` | public | internal | 内部複製メソッド |
| Core/Abstractions/EntityModel.cs | 52 | `SetStreamTableType()` | public | internal | 内部設定メソッド |
| Core/Abstractions/EntityModel.cs | 61 | `GetExplicitStreamTableType()` | public | internal | 内部取得メソッド |

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

## 📊 最終集計・影響度分析

### 🔢 変換候補総数
- **最高優先度**: 25個のクラス/メソッド
- **高優先度**: 8個のクラス/メソッド  
- **中優先度**: 4個のメソッド
- **総計**: **37個** ← 大幅増加！

### 🎯 段階別実装戦略（リスク最小化）

#### Phase 1: 安全確実（影響度極小）
```csharp
// Core内部ユーティリティ（5個）
internal static class KeyExtractor
internal class ProducerKey  
internal class CoreSettings
internal static class CoreDependencyConfiguration
internal static class CoreLayerPhase3Marker
```

#### Phase 2: Builder/Pipeline（影響度小）
```csharp  
// Query Builder群（6個）
internal class GroupByBuilder : IKsqlBuilder
internal class HavingBuilder : IKsqlBuilder
// + 他4個

// Query Pipeline群（5個）
internal class DMLQueryGenerator
internal class QueryDiagnostics
// + 他3個
```

#### Phase 3: Serialization層（影響度中）
```csharp
// Avro内部実装（8個）
internal class AvroSerializerFactory
internal class AvroSchema
internal class AvroSchemaInfo
// + 他5個
```

#### Phase 4: Messaging層（影響度高・慎重）
```csharp
// 直接Producer/Consumer（3個）
internal class KafkaConsumer<TValue, TKey>
internal class KafkaProducer<T>

// Manager系（要慎重検討）
internal class KafkaProducerManager  // DI設定要確認
internal class KafkaConsumerManager  // DI設定要確認
```

### ⚠️ 特別注意事項

#### Manager系クラスの扱い
`KafkaProducerManager`/`KafkaConsumerManager`は**DI設定次第**:
- DIコンテナ経由使用 → internal化可能
- 直接インスタンス化 → public維持必要
- **事前調査必須**

#### REST API関連（5個）
Query/Ksql配下のリクエスト/レスポンスクラス群は**確実にinternal化可能**

### 📈 期待効果（修正版）
- **API表面積削減**: 35-40%（従来予想20-30%から大幅増）
- **設計意図明確化**: Core/Query/Serialization層の責務分離
- **保守性向上**: 内部変更時の影響範囲限定

---

## 🔧 詳細実装ガイド

### Configuration系の部分的internal化
```csharp
// Before: 全てpublic
public class SchemaGenerationOptions
{
    public string? CustomName { get; set; }
    public SchemaGenerationOptions Clone() { ... }  // ←これをinternal化
    public string GetSummary() { ... }              // ←これもinternal化
}

// After: 使用頻度に応じて分離
public class SchemaGenerationOptions
{
    public string? CustomName { get; set; }
    internal SchemaGenerationOptions Clone() { ... }
    internal string GetSummary() { ... }
}
```

### Manager系の慎重な取り扱い
```csharp
// 調査必要：これらはDI経由使用？直接使用？
public class KafkaProducerManager  // ← 要確認
public class KafkaConsumerManager  // ← 要確認

// 確認ポイント：
// 1. Startup.cs等でのDI登録状況
// 2. ユーザーコードでの直接new使用有無
// 3. テストコードでの直接参照状況
```

### REST API内部クラスの一括変換
```csharp
// 確実にinternal化可能（外部使用なし）
internal class KsqlQueryRequest { ... }
internal class KsqlStatementRequest { ... }
internal class KsqlQueryResponse { ... }
internal class KsqlStatementResponse { ... }
internal class KsqlDbException : Exception { ... }
```

---

## 🚨 変換時の注意点・設計指摘

### 1. Builder Pattern設計の改善機会
現在のBuilder群は全てpublicだが、Factory経由が理想：
```csharp
// 現在
public class GroupByBuilder : IKsqlBuilder { ... }
public class SelectBuilder : IKsqlBuilder { ... }

// 改善案
internal class GroupByBuilder : IKsqlBuilder { ... }
public static class KsqlBuilderFactory
{
    public static IKsqlBuilder CreateGroupBy() => new GroupByBuilder();
    public static IKsqlBuilder CreateSelect() => new SelectBuilder();
}
```

### 2. Exception階層の見直し
```csharp
// 内部詳細例外 → internal
internal class SchemaRegistrationFatalException : Exception
internal class AvroSchemaRegistrationException : Exception

// ユーザー処理用例外 → public維持
public class KafkaMessageBusException : Exception
public class CoreValidationException : CoreException
```

### 3. Core層の責務分離強化
```csharp
// Phase3Marker等は完全に内部実装
internal static class CoreLayerPhase3Marker
internal static class CoreLayerValidation  
internal static class CoreDependencyConfiguration

// 外部契約は維持
public interface IKafkaContext
public interface IEntitySet<T>
```

---

## 📋 実装チェックリスト

### Phase 1 (安全確実・即実行可能)
- [ ] `KeyExtractor` → internal static
- [ ] `ProducerKey` → internal  
- [ ] `CoreSettings` → internal
- [ ] `CoreDependencyConfiguration` → internal static
- [ ] `CoreLayerPhase3Marker` → internal static
- [ ] `TopicOverrideService` → internal
- [ ] REST APIクラス群(5個) → internal

### Phase 2 (Builder群)
- [ ] `GroupByBuilder` → internal
- [ ] `HavingBuilder` → internal
- [ ] `JoinBuilder` → internal
- [ ] `ProjectionBuilder` → internal
- [ ] `SelectBuilder` → internal
- [ ] `WindowBuilder` → internal

### Phase 3 (Pipeline群)
- [ ] `DMLQueryGenerator` → internal
- [ ] `QueryDiagnostics` → internal
- [ ] `QueryExecutionResult` → internal
- [ ] `QueryExecutionMode` → internal enum
- [ ] `DerivedObjectType` → internal enum

### Phase 4 (Serialization層)
- [ ] `AvroSchema` → internal
- [ ] `AvroField` → internal
- [ ] `AvroSchemaInfo` → internal
- [ ] `UnifiedSchemaGenerator` → internal static
- [ ] `AvroSerializerFactory` → internal
- [ ] `AvroSerializerCache` → internal
- [ ] `AvroSchemaBuilder` → internal
- [ ] `AvroSchemaRepository` → internal

### Phase 5 (Messaging層・要慎重)
- [ ] **事前調査**: Manager系のDI使用状況確認
- [ ] `KafkaConsumer<T>` → internal
- [ ] `KafkaProducer<T>` → internal
- [ ] `ConsumerInstance` → internal
- [ ] `PooledConsumer` → internal
- [ ] `PooledProducer` → internal
- [ ] `PoolMetrics` → internal
- [ ] (**条件付き**) `KafkaProducerManager` → internal
- [ ] (**条件付き**) `KafkaConsumerManager` → internal

---

## 🎯 可視性設計ベストプラクティス

### 1. レイヤー別可視性原則
- **Application層**: Public（ユーザーAPI）
- **Core/Abstractions**: Public（契約）  
- **Core/実装**: Internal（詳細実装）
- **Query/Serialization/Messaging**: Internal（技術詳細）

### 2. AI自動生成対応ルール
```csharp
// ユーザーAPI = public
public abstract class KafkaContext
public class KsqlContextBuilder

// 内部実装 = internal  
internal class GroupByBuilder
internal static class KeyExtractor

// Builder/Factory = internal（DI経由）
internal class AvroSerializerFactory

// Exception = public（ユーザー処理用）/internal（内部詳細）
public class CoreValidationException   // ユーザー対応必要
internal class SchemaRegistrationFatalException  // 内部運用詳細
```

### 3. 今後の開発指針
- **新規クラス作成時**: internal first原則
- **外部使用明確な場合のみ**: public昇格
- **四半期ごと**: 可視性レビュー実施
- **Manager系クラス**: DI前提設計でinternal化推進

---

## 🎖️ 最終推奨：優先順位付き実行計画

| 優先度 | 対象範囲 | 候補数 | 実装難易度 | 影響リスク | 実行タイミング |
|--------|----------|--------|------------|------------|----------------|
| 🚀 S | Core/REST API | 12個 | 極易 | 極低 | 即時実行 |
| 🔥 A | Builder/Pipeline | 11個 | 易 | 低 | 1週間以内 |  
| 🟡 B | Serialization | 8個 | 中 | 中 | 2週間以内 |
| 🔵 C | Messaging(非Manager) | 6個 | 中 | 中 | 調査後実行 |
| ⚠️ D | Manager系 | 2個 | 高 | 高 | DI調査完了後 |

**総効果予想**: API表面積35-40%削減、設計意図明確化、保守性大幅向上ラス作成時は**internal first**
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