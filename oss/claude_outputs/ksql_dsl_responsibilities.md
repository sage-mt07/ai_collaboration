# KsqlDsl 責務定義資料

## 概要
本資料は、KsqlDslプロジェクトの全クラス・namespaceの責務と設計意図を整理したものです。
機能追加・リファクタリング時には本資料を参照・更新してください。

---

## Application層

### `KsqlDsl.Application`

#### `AvroSchemaInfoExtensions`
- **責務**: AvroSchemaInfo拡張メソッド提供
- **役割**: Schema Registryのサブジェクト名生成、Stream/Table型判定
- **設計意図**: Avro操作の共通ロジック集約
- **拡張可否**: 可（新メソッド追加は問題なし）

#### `KafkaContext`
- **責務**: 簡素化統合KafkaContext実装
- **役割**: Pool削除、直接管理によるEF風API提供
- **設計意図**: 複雑性削減、Core層統合による設計統一
- **拡張可否**: 可（Core層抽象化に準拠した拡張）

#### `KsqlContextBuilder`
- **責務**: KsqlContextOptions構築用Builderパターン
- **役割**: Schema Registry設定、ログ設定、バリデーション設定
- **設計意図**: 流暢なAPI提供、設定の型安全性確保
- **拡張可否**: 可（新設定項目の追加容易）

#### `KsqlContextOptions`
- **責務**: KsqlContext設定値保持
- **役割**: Schema Registry設定、タイムアウト、デバッグモード管理
- **設計意図**: 設定の集約化、バリデーション機能内蔵
- **拡張可否**: 可（プロパティ追加で機能拡張）

#### `KsqlContextOptionsExtensions`
- **責務**: KsqlContextOptions拡張メソッド
- **役割**: 流暢な設定API提供
- **設計意図**: Builder拡張による可読性向上
- **拡張可否**: 可（設定メソッド追加推奨）

---

## Configuration層

### `KsqlDsl.Configuration`

#### `KsqlDslOptions`
- **責務**: 全体設定のルートクラス
- **役割**: バリデーションモード、共通設定、トピック別設定管理
- **設計意図**: 階層化設定による管理性向上
- **拡張可否**: 可（新セクション追加可能）

#### `ValidationMode`
- **責務**: バリデーションモード列挙
- **役割**: Strict/Relaxedモード定義
- **設計意図**: 検証レベル制御
- **拡張可否**: 可（新モード追加可能）

### `KsqlDsl.Configuration.Abstractions`

#### `KafkaBatchOptions`
- **責務**: バッチ処理設定
- **役割**: バッチサイズ、待機時間、コミット設定
- **設計意図**: バッチ処理の細かな制御
- **拡張可否**: 可（バッチ制御オプション追加）

#### `KafkaFetchOptions`
- **責務**: フェッチ処理設定
- **役割**: 最大レコード数、タイムアウト設定
- **設計意図**: 取得処理のパフォーマンス調整
- **拡張可否**: 可（フェッチ制御オプション追加）

#### `KafkaSubscriptionOptions`
- **責務**: サブスクリプション設定
- **役割**: コンシューマー詳細設定（オフセット、セッション等）
- **設計意図**: Kafka Consumer設定の完全制御
- **拡張可否**: 可（Confluent.Kafka準拠で拡張）

#### `SchemaGenerationOptions`
- **責務**: スキーマ生成オプション
- **役割**: 命名、フォーマット、バリデーション設定
- **設計意図**: Avroスキーマ生成の柔軟性確保
- **拡張可否**: 可（生成オプション追加容易）

#### `SchemaGenerationStats`
- **責務**: スキーマ生成統計
- **役割**: プロパティ統計、包含率計算
- **設計意図**: 生成結果の透明性確保
- **拡張可否**: 可（統計項目追加可能）

### `KsqlDsl.Configuration.Options`

#### `AvroOperationRetrySettings`
- **責務**: Avro操作リトライ設定
- **役割**: 操作別リトライポリシー管理
- **設計意図**: 細かなリトライ制御による信頼性向上
- **拡張可否**: 可（新操作のポリシー追加）

#### `AvroRetryPolicy`
- **責務**: リトライポリシー詳細
- **役割**: 試行回数、遅延、例外制御
- **設計意図**: 柔軟なリトライ戦略実現
- **拡張可否**: 可（ポリシー要素追加可能）

---

## Core層

### `KsqlDsl.Core.Abstractions`

#### `ConsumerKey`
- **責務**: Consumer識別キー
- **役割**: EntityType+TopicName+GroupIdの組み合わせ
- **設計意図**: Consumer管理の一意性確保
- **拡張可否**: 限定的（構造変更は影響大）

#### `DateTimeFormatAttribute`
- **責務**: DateTime形式指定属性
- **役割**: Avroスキーマ生成でのDateTime制御
- **設計意図**: 日時型の柔軟な表現
- **拡張可否**: 可（フォーマット種別追加）

#### `DecimalPrecisionAttribute`
- **責務**: Decimal精度指定属性
- **役割**: 精度・スケール制御
- **設計意図**: 数値型の正確な表現
- **拡張可否**: 可（精度制御オプション追加）

#### `EntityModel`
- **責務**: エンティティメタデータ保持
- **役割**: 型情報、属性、プロパティ、検証結果管理
- **設計意図**: エンティティ情報の中央集約
- **拡張可否**: 可（メタデータ追加による機能拡張）

#### `IEntitySet<T>`
- **責務**: 統一エンティティ操作インターフェース
- **役割**: Producer/Consumer統合API
- **設計意図**: LINQ互換性維持、型安全性確保
- **拡張可否**: 慎重（既存実装への影響検討必要）

#### `IKafkaContext`
- **責務**: KafkaContext抽象定義
- **役割**: DbContext風統一インターフェース
- **設計意図**: 抽象化による実装柔軟性
- **拡張可否**: 慎重（Core抽象化への影響）

#### `ISerializationManager<T>`
- **責務**: シリアライゼーション管理抽象化
- **役割**: Avro/JSON/Protobuf対応基盤
- **設計意図**: シリアライゼーション形式の統一管理
- **拡張可否**: 可（新形式対応時拡張）

#### `KafkaIgnoreAttribute`
- **責務**: プロパティ除外指定
- **役割**: スキーマ生成・シリアライゼーションからの除外
- **設計意図**: 柔軟なプロパティ制御
- **拡張可否**: 可（除外オプション追加）

#### `KafkaMessage<TValue, TKey>`
- **責務**: Kafkaメッセージ表現
- **役割**: 型安全なメッセージ構造
- **設計意図**: メッセージデータの統一表現
- **拡張可否**: 可（メタデータフィールド追加）

#### `KafkaMessageContext`
- **責務**: メッセージコンテキスト情報
- **役割**: ID、ヘッダー、タグ管理
- **設計意図**: メッセージ追跡・制御情報の集約
- **拡張可否**: 可（コンテキスト情報追加）

#### `KeyAttribute`
- **責務**: キープロパティ指定
- **役割**: 順序、エンコーディング制御
- **設計意図**: キー構造の明示的定義
- **拡張可否**: 可（キー制御オプション追加）

#### `KsqlStreamAttribute`/`KsqlTableAttribute`
- **責務**: Stream/Table明示指定
- **役割**: ksqlDBオブジェクト型の明示
- **設計意図**: 型判定の曖昧性排除
- **拡張可否**: 可（ksqlDB機能追加時拡張）

#### `SerializerConfiguration<T>`
- **責務**: シリアライザー設定
- **役割**: キー・バリューシリアライザー情報
- **設計意図**: シリアライゼーション設定の統一管理
- **拡張可否**: 可（設定項目追加）

#### `TopicAttribute`
- **責務**: トピック設定属性
- **役割**: トピック名、パーティション等の設定
- **設計意図**: トピック設定の宣言的定義
- **拡張可否**: 可（Kafka機能追加時拡張）

#### `ValidationResult`
- **責務**: 検証結果保持
- **役割**: エラー・警告情報管理
- **設計意図**: 検証処理の統一的結果表現
- **拡張可否**: 可（検証項目追加）

### `KsqlDsl.Core.Context`

#### `KafkaContextCore`
- **責務**: KafkaContext基底実装
- **役割**: モデル構築、エンティティセット管理
- **設計意図**: 完全ログフリー、副作用なし設計
- **拡張可否**: 可（抽象メソッド実装で拡張）

#### `KafkaContextOptions`
- **責務**: Core層用コンテキスト設定
- **役割**: バリデーションモード管理
- **設計意図**: Core層の設定分離
- **拡張可否**: 可（Core固有設定追加）

### その他Core層クラス

#### `CoreDependencyConfiguration`
- **責務**: Core層依存関係設定
- **役割**: DI設定、依存関係検証
- **設計意図**: Core層の独立性確保
- **拡張可否**: 限定的（アーキテクチャ影響）

#### Extensions、Exceptions、Models、Validationクラス群
- **責務**: Core層横断的機能
- **役割**: 共通拡張、例外処理、モデル操作、検証
- **設計意図**: レイヤー内共通機能の集約
- **拡張可否**: 可（機能別拡張容易）

---

## Messaging層

### `KsqlDsl.Messaging.Abstractions`

#### `IKafkaConsumer<TValue, TKey>`
- **責務**: 型安全Consumer抽象化
- **役割**: 非同期ストリーム消費、バッチ処理
- **設計意図**: 高性能デシリアライゼーション実現
- **拡張可否**: 慎重（インターフェース変更影響大）

#### `IKafkaProducer<T>`
- **責務**: 型安全Producer抽象化
- **役割**: 単一・バッチ送信、フラッシュ制御
- **設計意図**: 高性能シリアライゼーション実現
- **拡張可否**: 慎重（インターフェース変更影響大）

### `KsqlDsl.Messaging.Configuration`

#### Configuration関連クラス群
- **責務**: Kafka設定セクション管理
- **役割**: Common、Producer、Consumer、SchemaRegistry設定
- **設計意図**: Confluent.Kafka準拠設定の構造化
- **拡張可否**: 可（Confluent.Kafka新機能対応）

### `KsqlDsl.Messaging.Consumers`

#### `KafkaConsumerManager`
- **責務**: 型安全Consumer管理
- **役割**: Pool削除、直接管理、型安全性強化
- **設計意図**: EF風API、事前確定管理
- **拡張可否**: 可（Consumer機能拡張）

#### `KafkaConsumer<TValue, TKey>`
- **責務**: 統合型安全Consumer実装
- **役割**: Confluent.Kafka完全委譲、シンプル化
- **設計意図**: Pool削除による複雑性排除
- **拡張可否**: 可（Consumer機能追加）

### `KsqlDsl.Messaging.Producers`

#### `KafkaProducerManager`
- **責務**: 型安全Producer管理
- **役割**: Pool削除、直接管理、型安全性強化
- **設計意図**: EF風API、事前確定管理
- **拡張可否**: 可（Producer機能拡張）

#### `KafkaProducer<T>`
- **責務**: 統合型安全Producer実装
- **役割**: Confluent.Kafka完全委譲、シンプル化
- **設計意図**: Pool削除による複雑性排除
- **拡張可否**: 可（Producer機能追加）

---

## Query層

### `KsqlDsl.Query.Abstractions`

#### `IEventSet<T>`
- **責務**: EventSet操作統一インターフェース
- **役割**: Core操作、クエリ操作、ストリーミング操作統合
- **設計意図**: EventSet分割に対する統一API
- **拡張可否**: 慎重（Query基盤への影響）

#### `IKsqlBuilder`
- **責務**: KSQL構文ビルダー抽象化
- **役割**: 式木からKSQL構文構築
- **設計意図**: ビルダー統一、責務明確化
- **拡張可否**: 可（新構文対応時拡張）

#### `IQueryTranslator`
- **責務**: LINQ→KSQL変換抽象化
- **役割**: 式木からKSQL文変換
- **設計意図**: 変換ロジック抽象化、テスタビリティ向上
- **拡張可否**: 可（変換機能拡張）

### `KsqlDsl.Query.Builders`

#### Builder関連クラス群
- **責務**: KSQL構文要素ビルダー
- **役割**: GroupBy、Having、Join、Projection、Select、Window構築
- **設計意図**: 旧実装排除、直接実装移行
- **拡張可否**: 可（KSQL新構文対応）

### `KsqlDsl.Query.Pipeline`

#### `QueryExecutionPipeline`
- **責務**: クエリ実行パイプライン
- **役割**: LINQ式から段階的ksqlDBクエリ実行
- **設計意図**: 新アーキテクチャ準拠、既存互換性維持
- **拡張可否**: 可（パイプライン機能拡張）

#### DDL/DMLQueryGenerator
- **責務**: DDL/DMLクエリ生成
- **役割**: CREATE文、SELECT文生成
- **設計意図**: 新アーキテクチャ準拠、IKsqlBuilder互換
- **拡張可否**: 可（新クエリ種別対応）

#### `DerivedObjectManager`
- **責務**: 派生オブジェクト管理
- **役割**: 一時的Stream/Table作成・クリーンアップ
- **設計意図**: 段階的クエリ実行支援
- **拡張可否**: 可（派生オブジェクト制御拡張）

#### `StreamTableAnalyzer`
- **責務**: Stream/Table型推論
- **役割**: LINQ式からStream/Table要件分析
- **設計意図**: 型推論の自動化
- **拡張可否**: 可（推論ルール追加）

### `KsqlDsl.Query.Ksql`

#### `KsqlDbRestApiClient`
- **責務**: ksqlDB REST API通信
- **役割**: クエリ実行、ストリーミング、レスポンス解析
- **設計意図**: 実データ送受信、JSON処理
- **拡張可否**: 可（ksqlDB API機能追加対応）

### `KsqlDsl.Query.Schema`

#### `SchemaRegistry`
- **責務**: エンティティ→ksqlDBオブジェクト自動生成
- **役割**: スキーマ登録、オブジェクト名管理
- **設計意図**: 新アーキテクチャ準拠、自動化
- **拡張可否**: 可（スキーマ管理機能拡張）

---

## Serialization層

### `KsqlDsl.Serialization.Abstractions`

#### `AvroEntityConfiguration`
- **責務**: Avroエンティティ設定
- **役割**: トピック、キー、検証設定管理
- **設計意図**: 属性ベース自動設定、設定の構造化
- **拡張可否**: 可（設定項目追加）

#### `AvroSerializationManager<T>`
- **責務**: 型安全Avroシリアライゼーション管理
- **役割**: シリアライザー取得、検証、統計
- **設計意図**: 型安全性、キャッシュ、統計機能
- **拡張可否**: 可（シリアライゼーション機能拡張）

#### インターフェース群
- **責務**: シリアライゼーション抽象化
- **役割**: Provider、Manager、統計、設定の抽象定義
- **設計意図**: 実装柔軟性、テスタビリティ
- **拡張可否**: 慎重（実装への影響検討）

### `KsqlDsl.Serialization.Avro`

#### Cache関連
- **責務**: Avroシリアライザーキャッシュ
- **役割**: インスタンス管理、統計収集
- **設計意図**: パフォーマンス最適化
- **拡張可否**: 可（キャッシュ戦略拡張）

#### Core関連
- **責務**: Avro基盤実装
- **役割**: シリアライザー、スキーマ生成、ファクトリー
- **設計意図**: Confluent.Kafka統合、統一スキーマ生成
- **拡張可否**: 可（Avro機能拡張）

#### Management関連
- **責務**: Avroスキーマ管理
- **役割**: 登録、バージョン管理、リポジトリ
- **設計意図**: スキーマライフサイクル管理
- **拡張可否**: 可（管理機能拡張）

#### `ResilientAvroSerializerManager`
- **責務**: 耐障害性Avro管理
- **役割**: リトライ制御、Fail Fast設計
- **設計意図**: 信頼性向上、運用監視対応
- **拡張可否**: 可（耐障害性機能追加）

---

## EventSet層

### `KsqlDsl.EventSet<T>`
- **責務**: Core層IEntitySet実装
- **役割**: Producer/Consumer統合、LINQ互換
- **設計意図**: Phase3簡素化、ログフリー設計
- **拡張可否**: 可（Core抽象化準拠で拡張）

### `KsqlDsl.KafkaContext`
- **責務**: Core層統合KafkaContext
- **役割**: 上位層機能統合、Producer/Consumer管理
- **設計意図**: Core抽象化継承、機能統合
- **拡張可否**: 可（Core設計に準拠した拡張）

---

## 全体設計方針

### アーキテクチャ原則
1. **レイヤー分離**: Core → Messaging/Serialization/Query の一方向依存
2. **型安全性**: ジェネリクス活用による型安全なAPI
3. **非同期**: async/await徹底、CancellationToken対応
4. **拡張性**: インターフェース抽象化、設定駆動
5. **信頼性**: リトライ、Fail Fast、詳細ログ

### 拡張ガイドライン
- **Core層**: 抽象化への影響を慎重検討
- **Configuration**: 新設定はOptions経由で追加
- **Messaging**: Confluent.Kafka準拠で機能拡張
- **Query**: KSQL仕様準拠、IKsqlBuilder実装
- **Serialization**: 型安全性維持、キャッシュ考慮

### 禁止事項
- Core層から上位層への依存
- 直接的なConfluent.Kafkaクラス露出
- 同期ブロッキング処理
- ログフリー設計の破壊

---

## 更新履歴
- 2025-06-22: 初版作成（全150クラスの責務定義完了）