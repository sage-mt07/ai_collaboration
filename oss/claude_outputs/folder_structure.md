# 📁 AVRO専用設計 - フォルダ別ファイル一覧

## 🏗️ **Application Layer**

### `src/Application/`
```
KsqlContext.cs                    ← EF風エントリーポイント (Phase 3)
AvroModelBuilder.cs              ← EF風設定ビルダー (Phase 3)
AvroEntityTypeBuilder.cs         ← エンティティ設定 (Phase 3)
AvroPropertyBuilder.cs           ← プロパティ設定 (Phase 3)
KsqlContextOptions.cs            ← Context設定 (Phase 3)
IAvroSerializationManager.cs     ← シリアライゼーション管理IF (Phase 3)
AvroSerializationManager.cs      ← 軽量シリアライゼーション管理 (Phase 3)
```

### `src/Application/Extensions/`
```
AvroSchemaRegistrationServiceExtensions.cs  ← 型安全拡張 (Phase 3)
AvroSchemaInfoExtensions.cs                 ← スキーマ情報拡張 (Phase 3)
KsqlContextOptionsExtensions.cs             ← オプション拡張 (Phase 3)
AvroEntityTypeBuilderExtensions.cs          ← ビルダー拡張 (Phase 3)
```

---

## 🏗️ **AVRO Schema Management Layer**

### `src/Serialization/Avro/Management/`
```
IAvroSchemaRegistrationService.cs    ← スキーマ登録インターフェース (Phase 2)
AvroSchemaRegistrationService.cs     ← 起動時一括登録 (Phase 2)
IAvroSchemaRepository.cs             ← スキーマリポジトリIF (Phase 2)
AvroSchemaRepository.cs              ← 登録済みスキーマ保持 (Phase 2)
AvroSchemaBuilder.cs                 ← スキーマビルダー (保持)
IAvroSchemaProvider.cs               ← スキーマプロバイダIF (保持)
```

### `src/Serialization/Avro/Core/`
```
AvroSchemaInfo.cs                    ← 簡素化スキーマ情報 (Phase 2)
AvroSchemaGenerator.cs               ← スキーマ生成ヘルパー (Phase 2)
SchemaGenerator.cs                   ← Avroスキーマ生成 (保持)
AvroSchema.cs                        ← スキーマ定義 (保持)
AvroField.cs                         ← フィールド定義 (保持)
AvroSerializer.cs                    ← シリアライザー (保持)
AvroDeserializer.cs                  ← デシリアライザー (保持)
AvroSerializerFactory.cs             ← ファクトリー (保持)
SchemaRegistryClientWrapper.cs      ← クライアントラッパー (保持)
```

### `src/Serialization/Avro/Exceptions/`
```
AvroSchemaRegistrationException.cs   ← スキーマ登録例外 (Phase 2)
SchemaRegistryOperationException.cs  ← Schema Registry操作例外 (保持)
```

---

## 🏗️ **AVRO Caching Layer**

### `src/Serialization/Avro/Cache/`
```
AvroSerializerCache.cs              ← 簡素化キャッシュ (Phase 1)
AvroSerializerCacheKey.cs           ← キャッシュキー (保持)
CacheStatistics.cs                  ← 基本統計のみ (保持)
EntityCacheStatus.cs                ← エンティティキャッシュ状態 (保持)
```

---

## 🏗️ **AVRO Serialization Layer**

### `src/Serialization/Abstractions/`
```
AvroSerializationManager.cs         ← 簡素化管理 (Phase 1)
IAvroSerializationManager.cs        ← 管理インターフェース (Phase 1)
IAvroSerializer.cs                  ← シリアライザーIF (保持)
IAvroDeserializer.cs                ← デシリアライザーIF (保持)
ISchemaRegistryClient.cs            ← Schema RegistryクライアントIF (保持)
interfaces.cs                       ← その他インターフェース (保持)
```

### `src/Serialization/Avro/Adapters/`
```
AvroSerializerAdapter.cs            ← Confluentアダプター (保持)
AvroDeserializerAdapter.cs          ← Confluentアダプター (保持)
```

---

## 🏗️ **Core Abstractions (保持)**

### `src/Core/Abstractions/`
```
EntityModel.cs                      ← エンティティモデル (保持)
TopicAttribute.cs                   ← トピック属性 (保持)
KeyAttribute.cs                     ← キー属性 (保持)
KafkaIgnoreAttribute.cs             ← 無視属性 (保持)
DecimalPrecisionAttribute.cs        ← 精度属性 (保持)
DateTimeFormatAttribute.cs          ← 日時形式属性 (保持)
ValidationResult.cs                 ← 検証結果 (保持)
AvroEntityConfiguration.cs          ← エンティティ設定情報 (Phase 2)
CoreSerializationStatistics.cs     ← 基本統計 (保持)
KsqlStreamAttribute.cs              ← Stream属性 (保持)
KsqlTableAttribute.cs               ← Table属性 (保持)
SerializerConfiguration.cs          ← シリアライザー設定 (保持)
```

### `src/Core/Extensions/`
```
CoreExtensions.cs                   ← Core拡張メソッド (保持)
LoggerFactoryExtensions.cs          ← ログ拡張メソッド (Phase 2で活用)
```

### `src/Core/Configuration/`
```
CoreSettings.cs                     ← Core設定 (保持)
ICoreSettingsProvider.cs           ← 設定プロバイダIF (保持)
CoreSettingsProvider.cs            ← 設定プロバイダ (保持)
CoreSettingsChangedEventArgs.cs    ← 設定変更イベント (保持)
```

---

## 🧪 **Testing**

### `tests/Integration/`
```
AvroIntegrationTests.cs             ← 統合テスト (Phase 4)
TestEntities.cs                     ← テスト用エンティティ (Phase 4)
TestKsqlContext.cs                  ← テスト用Context (Phase 4)
MockSchemaRegistryClient.cs         ← モック実装 (Phase 4)
```

---

## ❌ **削除されたフォルダ・ファイル (60%削減)**

### `❌ src/Serialization/Avro/Metrics/` (完全削除)
```
❌ AvroMetricsCollector.cs
❌ AvroPerformanceMetrics.cs  
❌ PerformanceThresholds.cs
❌ MetricsReportingService.cs
```

### `❌ src/Serialization/Avro/Performance/` (完全削除)
```
❌ PerformanceMonitoringAvroCache.cs
❌ PerformanceMetrics.cs
❌ SlowOperationRecord.cs
❌ ExtendedCacheStatistics.cs
❌ CacheEfficiencyReport.cs
❌ MetricsSummary.cs
```

### `❌ src/Serialization/Avro/Management/` (部分削除)
```
❌ AvroSchemaVersionManager.cs
❌ SchemaVersionManager.cs
❌ ResilientAvroSerializerManager.cs
❌ EnhancedAvroSerializerManager.cs
```

### `❌ src/Serialization/Avro/Tracing/` (完全削除)
```
❌ AvroActivitySource.cs
❌ AvroLogMessages.cs
❌ 全てのTracing関連
```

### `❌ src/Monitoring/` (完全削除)
```
❌ 全てのMonitoring関連
❌ 全てのRetry関連
❌ 全てのResilience関連
```

---

## 📊 **フォルダ構成サマリー**

| フォルダ | ファイル数 | 状態 | 説明 |
|---------|-----------|------|-----|
| **src/Application/** | 8 | ✅ 新規追加 | EF風API実装 |
| **src/Serialization/Avro/Management/** | 6 | ✅ 簡素化 | Schema管理軽量化 |
| **src/Serialization/Avro/Cache/** | 4 | ✅ 簡素化 | 軽量キャッシュ |
| **src/Serialization/Avro/Core/** | 9 | ✅ 保持 | 核心機能維持 |
| **src/Core/Abstractions/** | 12 | ✅ 保持 | 基盤クラス維持 |
| **src/Core/Extensions/** | 2 | ✅ 活用 | ログ統合 |
| **tests/Integration/** | 4 | ✅ 新規追加 | 品質保証 |
| **❌ src/Serialization/Avro/Metrics/** | 0 | ❌ 完全削除 | 不要機能除去 |
| **❌ src/Serialization/Avro/Performance/** | 0 | ❌ 完全削除 | 複雑監視除去 |
| **❌ src/Monitoring/** | 0 | ❌ 完全削除 | 全監視機能除去 |

---

## 🎯 **設計原則確認**

### **✅ 保持されたもの (価値のあるコンポーネント)**
- **AVRO Schema生成・管理**: SchemaGenerator, AvroSchemaBuilder
- **Schema Registry統合**: Confluent SDK活用部分
- **エンティティ管理**: EntityModel, 各種Attribute
- **基本シリアライゼーション**: AvroSerializer/Deserializer
- **Core拡張機能**: CoreExtensions, LoggerExtensions

### **❌ 削除されたもの (不要な複雑性)**
- **Metrics & Performance監視**: 全機能削除
- **複雑なバージョン管理**: 高度な互換性チェック等
- **Resilience & Retry**: 複雑な回復機能
- **Tracing & Monitoring**: 詳細な追跡機能
- **Enhanced機能**: 高度だが不要な機能

### **✅ 新規追加されたもの (EF風価値)**
- **Application Layer**: KsqlContext, AvroModelBuilder
- **Fluent API**: 型安全な設定インターフェース
- **Integration Tests**: 品質保証テスト

**🚀 結果: 「必要最小限で最大価値」の設計思想を完全実現！**