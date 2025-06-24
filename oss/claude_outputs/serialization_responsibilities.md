# Serialization責務分離ドキュメント

## 概要

KsqlDslのSerialization機能は、従来の巨大なクラス（400行+）から責務ごとに分離され、保守性・テスト性・拡張性を大幅に向上させました。

## 責務分離マップ

### 1. 抽象層 (Abstractions/)

#### ISerializationManager<T>
- **責務**: 型安全なシリアライゼーション管理の統一API
- **提供機能**: SerializerPair/DeserializerPair取得、Round-trip検証、統計情報
- **依存**: なし（最上位抽象）

#### IAvroSchemaProvider
- **責務**: スキーマ生成・提供の抽象化
- **提供機能**: Key/Valueスキーマ生成、スキーマ検証
- **依存**: なし

#### ISchemaVersionResolver
- **責務**: スキーマバージョン管理の抽象化
- **提供機能**: バージョン解決、アップグレード判定・実行
- **依存**: なし

### 2. Core層 (Avro/Core/)

#### AvroSerializerFactory
- **責務**: Avroシリアライザー・デシリアライザーの生成
- **提供機能**: 
  - エンティティモデルベースのシリアライザー生成
  - プリミティブ・複合キー対応
  - Schema Registry統合
- **依存**: ConfluentSchemaRegistry、EntityModel
- **特徴**: 純粋なファクトリパターン、状態なし

### 3. Cache層 (Avro/Cache/)

#### AvroSerializerCache
- **責務**: シリアライザー・デシリアライザーのキャッシュ管理
- **提供機能**:
  - エンティティ型別キャッシュ制御
  - パフォーマンス統計収集
  - キャッシュクリア・ライフサイクル管理
- **依存**: AvroSerializerFactory
- **特徴**: Thread-safe、統計情報付き

#### AvroEntitySerializationManager<T>
- **責務**: 型特化シリアライゼーション管理
- **提供機能**:
  - 型安全なシリアライザー取得
  - Round-trip検証
  - 型別統計管理
- **依存**: AvroSerializerFactory
- **特徴**: 型安全性、個別統計

### 4. Management層 (Avro/Management/)

#### AvroSchemaVersionManager
- **責務**: スキーマ進化・バージョン管理
- **提供機能**:
  - スキーマバージョン解決
  - 互換性チェック・アップグレード
  - バージョン履歴管理
- **依存**: ISchemaRegistryClient、AvroUtils
- **特徴**: 後方互換性保証、安全なアップグレード

#### AvroSchemaBuilder
- **責務**: Avroスキーマ構築・生成
- **提供機能**:
  - Key/Valueスキーマ生成
  - 論理型対応（decimal、DateTime、UUID）
  - スキーマ検証
- **依存**: SchemaRegistry、Reflection
- **特徴**: 論理型完全対応、Null安全性

### 5. Internal層 (Avro/Internal/)

#### AvroUtils
- **責務**: 内部専用ユーティリティ機能
- **提供機能**:
  - トピック名抽出
  - キー値抽出・型判定
  - エンティティモデル作成
  - 循環参照検証
- **依存**: Attributes、Modeling
- **特徴**: 非公開、ヘルパー関数のみ

### 6. 統合層 (Avro/)

#### AvroSerializationManager<T>
- **責務**: 全機能の統合・調整
- **提供機能**:
  - ワンストップAPI提供
  - スキーマアップグレード連携
  - 統合診断情報
- **依存**: 全層
- **特徴**: Facade パターン、使いやすさ重視

## 依存関係フロー

```
Abstractions (Interface層)
    ↑
Core → Cache → Management → Internal
    ↑           ↑
    └─── 統合層 ─────┘
```

## 責務境界

### Core層の境界
- ✅ DO: 純粋なシリアライザー生成
- ❌ DON'T: キャッシュ・統計・バージョン管理

### Cache層の境界
- ✅ DO: パフォーマンス最適化・統計収集
- ❌ DON'T: スキーマ生成・バージョン制御

### Management層の境界
- ✅ DO: スキーマ進化・互換性管理
- ❌ DON'T: 具体的なシリアライゼーション処理

### Internal層の境界
- ✅ DO: 共通ヘルパー・ユーティリティ
- ❌ DON'T: 公開API・ビジネスロジック

## 拡張性

### 新形式対応（JSON、Protobuf等）
```
src/Serialization/
├── Json/
│   ├── Core/JsonSerializerFactory.cs
│   └── Cache/JsonSerializerCache.cs
└── Protobuf/
    ├── Core/ProtobufSerializerFactory.cs
    └── Cache/ProtobufSerializerCache.cs
```

### 新機能追加
- Monitoring層: ヘルス監視専用
- Diagnostics層: 診断・デバッグ専用
- Security層: 暗号化・認証専用

## 旧構造からの移行

| 旧クラス | 新責務先 | 変更内容 |
|---------|---------|---------|
| AvroSerializerCache (400行) | Core + Cache層 | 生成・キャッシュ分離 |
| AvroSchemaVersionManager | Management層 | 移動のみ |
| AvroUtils | Internal層 | 非公開化・最小化 |

## テスト戦略

- **Unit Test**: 各層独立テスト
- **Integration Test**: 層間連携テスト  
- **Round-trip Test**: `ISerializationManager<T>`経由
- **Performance Test**: Cache層専用