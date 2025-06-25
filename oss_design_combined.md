# OSS設計資料：統合ドキュメント

## Overview

### Readme

﻿# KSQL Entity Framework 要件定義書

## 目次 (Table of Contents)

- [1. 概要](#1-概要)
- [2. 基本原則](#2-基本原則)
- [3. 主要コンポーネント](#3-主要コンポーネント)
  - [3.1 トピック (Kafka Topics)](#31-トピック-kafka-topics)
  - [3.2 ストリーム (KSQL Streams)](#32-ストリーム-ksql-streams)
  - [3.3 テーブル (KSQL Tables)](#33-テーブル-ksql-tables)
  - [3.4 クエリと購読](#34-クエリと購読)
- [4. POCO (Plain Old CLR Objects) の設計](#4-poco-plain-old-clr-objects-の設計)
  - [4.1 基本定義](#41-基本定義)
  - [4.2 特殊型のサポート](#42-特殊型のサポート)
- [5. プロデュース/コンシューム操作](#5-プロデュースコンシューム操作)
  - [5.1 プロデューサー (データ送信)](#51-プロデューサー-データ送信)
  - [5.2 コンシューマー (データ受信)](#52-コンシューマー-データ受信)
  - [5.3 トランザクション処理](#53-トランザクション処理)
- [6. エラー処理とデータ品質](#6-エラー処理とデータ品質)
  - [6.1 エラー処理戦略](#61-エラー処理戦略)
  - [6.2 デッドレターキュー](#62-デッドレターキュー)
- [7. テーブル管理操作](#7-テーブル管理操作)
  - [7.1 テーブル作成と更新](#71-テーブル作成と更新)
  - [7.2 テーブルの再構築と管理](#72-テーブルの再構築と管理)
- [8. リリース計画](#8-リリース計画)
- [9. アーキテクチャ概要](#9-アーキテクチャ概要)
  - [コアコンポーネント](#コアコンポーネント)
  - [主要インターフェース](#主要インターフェース)
  - 
## 1. 概要

KSQL Entity Frameworkは、C#プログラマがEntityFrameworkライクなAPIを使用してKSQL/KafkaStreamsを操作できるようにするライブラリです。トピック中心の設計、POCOベースのクエリ定義、LINQライクなストリーム操作を特徴とします。

## 2. 基本原則

1. **トピック中心設計**: すべての操作はKafkaトピックを起点とする
2. **型安全性**: C#の型システムを活用してスキーマの整合性を確保
3. **使い慣れたAPI**: EntityFrameworkに類似したAPIデザイン
4. **LINQサポート**: ストリーム処理をLINQクエリとして表現
5. **段階的デプロイ**: 基本機能から高度な機能へと段階的に実装
6. **購読モードの固定化**: ストリーム定義時に自動コミット／手動コミットの方式を明示し、実行時に切り替え不可とする


## 3. POCO属性ベースDSL設計ルール（Fluent APIの排除方針）

本OSSでは、Kafka/KSQLの設定をすべてPOCOクラスの属性で定義する方式を採用する。
これは、Fluent APIを用いたDSL記述の柔軟性と引き換えに、「構成がPOCOに集約されている」という明快さを重視した設計方針である。

🏷️ クラスレベル属性一覧
|属性名	|説明|
|---|---|
[Topic("topic-name")]	|Kafkaトピック名の指定（Partitions, Replicationも設定可能）
[KsqlStream] / [KsqlTable]	|Stream/Table の明示指定（未指定時は自動判定）
[Table("name")]	|EF Coreとの互換性維持用（任意）
[WindowHeartbeat("heartbeat-topic")]	|RocksDB側のウィンドウ更新確認用ハートビート設定

🧩 プロパティレベル属性一覧
|属性名	|説明|
|---|---|
[Key(Order = n)]|	KafkaのKeyに対応するプロパティ。複合キー可
[KafkaIgnore]	|スキーマ定義・KSQL変換から除外される
[DecimalPrecision(precision, scale)]	|decimal型の精度指定（例：18,4）
[DateTimeFormat("format")]	|KSQL上でのDateTimeの文字列フォーマット
[DefaultValue(value)]	|定義時のデフォルト値（スキーマ定義上のみ）
[MaxLength(n)]	|文字列長の制約。Avroスキーマにも反映

🤖 自動判定ロジック
[Key]の有無によって [KsqlStream] or [KsqlTable] の暗黙的推定を行う

Nullable<T> はスキーマ上で Union<Type, null> として定義される

Key属性が複数ある場合は複合キー（CompositeKey）として変換される

💡 サンプル：Orderエンティティの定義
```csharp
[Topic("orders", Partitions = 3, Replication = 1)]
[KsqlTable]
public class Order
{
    [Key(Order = 0)]
    public int OrderId { get; set; }

    [DateTimeFormat("yyyy-MM-dd")]
    public DateTime OrderDate { get; set; }

    [DecimalPrecision(18, 4)]
    public decimal TotalAmount { get; set; }

    [MaxLength(100)]
    public string? Region { get; set; }

    [KafkaIgnore]
    public string? InternalUseOnly { get; set; }
}
```
📘 設計上の方針と意図
構成情報はすべて POCOに記述され、外部設定ファイルやFluent DSLは不要

利用者は .cs ファイル上の属性のみを参照すれば動作構成を把握可能


🔁 Fluent API の補助的活用と制限について
POCO属性を中心とした設計方針を採る本DSLでは、Fluent API はあくまで補助的手段として位置づけられ、以下のコンポーネントで限定的に利用可能です。

🧱 1. KsqlContextBuilder（KSQL DSL全体の構成）
```csharp
var context = CsharpKsqlContextBuilder.Create()
    .UseSchemaRegistry("http://localhost:8081")
    .EnableLogging(loggerFactory)
    .ConfigureValidation(autoRegister: true, failOnErrors: false, enablePreWarming: true)
    .WithTimeouts(TimeSpan.FromSeconds(5))
    .EnableDebugMode(true)
    .Build()
    .BuildContext<MyKsqlContext>();
```
主な用途：

スキーマレジストリ連携

ログ出力の設定

バリデーションやタイムアウト等の動作制御

🧩 2. ModelBuilder（Entity定義時）
```csharp
protected override void OnModelCreating(IModelBuilder modelBuilder)
{
    modelBuilder.Entity<Order>()
        .AsTable(); // または .AsStream()
}
```
POCO属性に Stream/Table 指定がない場合のみ使用可

明示的な型指定を可能にする（ただし key/topic 設定は禁止）

📦 3. AvroEntityConfigurationBuilder（Avroスキーマ定義の詳細制御）
```csharp
configuration.Configure<Order>()
    .ToTopic("orders")                   // ❌ 非推奨（属性優先）
    .HasKey(o => o.Id)                   // ❌ 非推奨
    .WithPartitions(3)
    .WithReplicationFactor(2)
    .AsStream();                         // ✅ Stream/Table指定のみ許可
```    
このビルダーは、Avroスキーマ生成時に高度な制御が必要な場合に限り使用される。
ただし、以下のメソッド呼び出しは設計原則違反となる。

🚫 制限事項
メソッド	理由
.ToTopic("...")	トピック名は [Topic] 属性で指定するため禁止
.HasKey(...)	キー定義は [Key] 属性に一本化されている
.AsStream() / .AsTable()	属性またはModelBuilderと重複可能。両方指定で一致しない場合はエラー

これらのメソッドは呼び出された場合に NotSupportedException をスローする設計とし、誤用を防止する。

### 3.1 トピック (Kafka Topics)


#### トピック定義
```csharp
// 属性によるマッピング
[Topic("orders", PartitionCount = 12, ReplicationFactor = 3)]
public class Order 
{
    [Key]
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime OrderTime { get; set; }
}

// Fluent API
modelBuilder.Entity<Order>()
    .WithPartitions(12)
    .WithReplicationFactor(3);
```

#### トピック構成
- パーティション設定: パーティション数、パーティショニング戦略
- レプリケーション設定: レプリケーションファクター、ISRの最小数
- 保持ポリシー: メッセージの保持期間、サイズ制限
- 圧縮設定: トピックレベルの圧縮方式

#### スキーマ管理
- 自動スキーマ登録: POCOからAvroスキーマを生成し登録
- 互換性設定: スキーマ互換性ポリシーの指定
- スキーマ進化: スキーマバージョンの管理とマイグレーション

#### トピック操作
```csharp
/ 型定義に基づく登録
await context.EnsureDeclaredAsync<Order>();

// 型定義に基づく削除
await context.UndeclareAsync<Order>();
```

### 3.2 ストリーム (KSQL Streams)
#### ストリーム定義の前提

ストリームは POCO に対して LINQ 式が適用されたときに動的に解釈され、生成される。

#### 判定ルール

POCO（C#のエンティティ定義）に対してLINQ式が記述された場合、
その型は "ストリーム" として解釈され、対応する CREATE STREAM 定義が自動生成されます。

ただし、以下のように GroupBy や Aggregate、Window を含む場合は CREATE TABLE（テーブル）として解釈されます。
```csharp
// ストリームとして解釈される例（AutoCommitがデフォルト）
modelBuilder.Entity<Order>()
    .Where(o => o.Amount > 1000)
    .Select(o => new { o.OrderId, o.CustomerId });

// テーブルとして解釈される例（GroupBy を含む）
modelBuilder.Entity<Order>()
    .Window(TumblingWindow.Of(TimeSpan.FromHours(1)))
    .GroupBy(o => o.CustomerId)
    .Select(g => new HourlyStats 
    { 
        CustomerId = g.Key,
        Hour = g.Window.Start,
        OrderCount = g.Count() 
    });

// 明示的にストリームとして扱う（GroupByがあっても）
modelBuilder.Entity<Order>()
    .AsStream()
    .GroupBy(o => o.Region)
    .Select(g => new { g.Key });

// 明示的にテーブルとして扱う
modelBuilder.Entity<Order>()
    .AsTable()
    .Select(o => new { o.CustomerId, o.Amount });
```

このように modelBuilder.Entity<Order>() に対して LINQ が付くか否かで "配信対象"
さらに LINQ の内容によって "ストリーム" か "テーブル" かを判別します。
加えて、.AsStream() や .AsTable() を使用することで、意図的に解釈を上書きすることも可能です。

#### コミット方式の指定

この `WithManualCommit()` 指定は `EntityModel` に保存され、実行時の `ForEachAsync()` での処理方式（自動／手動）を決定する際に参照されます。実行時にこの設定を変更することはできません。

```csharp
// 自動コミット（デフォルト）
modelBuilder.Entity<Order>()
    .Where(o => o.Amount > 1000)
    .Select(o => new { o.OrderId, o.CustomerId });

// 手動コミットを明示
modelBuilder.Entity<Order>()
    .Where(o => o.Amount > 1000)
    .Select(o => new { o.OrderId, o.CustomerId })
    .WithManualCommit();
```
注意：購読時の commit モードは LINQ 実行時に切り替えることはできません。定義時に確定させる必要があります。

### 3.3 テーブル (KSQL Tables)

このライブラリでは、LINQ 式が GroupBy や Aggregate を含み、テーブルと判定された場合、対応するトピックは 自動的に compact モードで作成 されます。

開発者はコンパクションの有無を意識せず、通常の LINQ クエリ定義だけで正しく永続化特性を持ったトピックを扱えます。
```csharp
var latestOrders = context.Orders
    .GroupBy(o => o.CustomerId)
    .Select(g => new {
        CustomerId = g.Key,
        LatestAmount = g.LatestByOffset(o => o.Amount)
    });
```
この例では CustomerId をキーとした最新の注文金額だけを保持するテーブルが作成され、その裏のトピックは compact となります。

#### テーブル判定ルールと基本設計
POCO（C#のエンティティ定義）に対する LINQ 式の中で GroupBy, Aggregate, Window などの構文が含まれている場合、そのクエリは CREATE TABLE に相当する KSQL テーブルと解釈されます。これにより、ストリームとテーブルの判定が LINQ 構文の意味に基づいて一貫して行われます。

また、.AsTable() を明示的に呼び出すことで、意図的にテーブルとして扱うことも可能です。

#### 集約操作
```csharp
// サマリ集計
var customerStats = context.Orders
    .GroupBy(o => o.CustomerId)
    .Aggregate(g => new CustomerStats 
    { 
        CustomerId = g.Key, 
        TotalAmount = g.Sum(o => o.Amount),
        OrderCount = g.Count()
    });

// LATEST_BY_OFFSET
var latestCustomerOrders = context.Orders
    .GroupBy(o => o.CustomerId)
    .Aggregate(g => new CustomerLatestOrder
    {
        CustomerId = g.Key,
        LatestOrderId = g.LatestByOffset(o => o.OrderId),
        LatestOrderTime = g.LatestByOffset(o => o.OrderTime),
        LatestAmount = g.LatestByOffset(o => o.Amount)
    });

// EARLIEST_BY_OFFSET
var firstTimeCustomers = context.Orders
    .GroupBy(o => o.CustomerId)
    .Aggregate(g => new CustomerFirstOrder
    {
        CustomerId = g.Key,
        FirstOrderId = g.EarliestByOffset(o => o.OrderId),
        FirstOrderTime = g.EarliestByOffset(o => o.OrderTime),
        FirstAmount = g.EarliestByOffset(o => o.Amount)
    });


```
#### テーブル結合（JOIN）

```csharp
// 単一キー結合
var query = from o in context.Orders
            join c in context.Customers
            on o.CustomerId equals c.CustomerId
            select new { o.OrderId, c.CustomerName, o.Amount };

// 複合キー結合
var query2 = from o in context.Orders
             join c in context.Customers
             on new { o.CustomerId, o.Region } equals 
                new { c.CustomerId, c.Region }
             select new { o.OrderId, c.CustomerName, o.Amount };

// 3テーブル結合
var query3 = from o in context.Orders
             join c in context.Customers on o.CustomerId equals c.CustomerId
             join p in context.Products on o.ProductId equals p.ProductId
             select new {
                 o.OrderId,
                 c.CustomerName,
                 p.ProductName,
                 o.Quantity,
                 o.Amount
             };

```

⏱️ ウィンドウDSLの拡張：複数Window定義とアクセス
本フレームワークは、同一エンティティに対して複数の異なるウィンドウ幅（例：1分、5分、15分、60分）を定義し、個別にアクセス・購読可能とする拡張DSLをサポートします。

```csharp
modelBuilder.Entity<Chart>()
    .Window(new int[]{1,5,15,60});
```
この記述により以下の4テーブルが自動生成されます：

Chart_1min

Chart_5min

Chart_15min

Chart_60min

各ウィンドウに対して自動的に WindowHeartbeat 属性が付与され、例えば以下のようなトピックが生成・送信されます：

```csharp
[WindowHeartbeat("chart_1min_heartbeat")]
```
これにより、ksqlDBでのウィンドウ更新を時間経過に関係なく強制トリガーすることが可能です。

💻 LINQからのアクセス方法
ユーザーコードからは次のようにウィンドウサイズを指定してデータ取得できます：

```csharp
var candles1m = ctx.Charts.Window(1).ToList();
var candles5m = ctx.Charts.Window(5).ToList();
```
戻り値は IQueryable<Chart> として取得され、通常のLINQ式が適用可能です。

🔁 最新データ取得の例
ToLatest() は専用メソッドではなく、LINQで以下のように記述することを推奨します：

```csharp
var latest = ctx.Charts.Window(5).ToList()
    .OrderByDescending(c => c.Timestamp)
    .GroupBy(c => c.Symbol)
    .Select(g => g.First());
```

### 3.4 クエリと購読

#### ストリーム定義とコミット方式の指定
```csharp
// modelBuilder による定義（自動コミット：デフォルト）
modelBuilder.Entity<Order>()
    .Where(o => o.Amount > 1000)
    .Select(o => new { o.OrderId, o.CustomerId });
// 自動コミットの購読処理（デフォルト）
await foreach (var order in context.Orders.ForEachAsync())
{
    Console.WriteLine($"Received: {order.Value.OrderId}");
    // 自動でコミットされるため明示的な commit は不要
}

// modelBuilder による定義（手動コミット）
modelBuilder.Entity<HighValueOrder>()
    .Where(o => o.Amount > 1000)
    .Select(o => new { o.OrderId, o.CustomerId })
    .WithManualCommit();

// 手動コミット付きの購読処理
await foreach (var order in context.HighValueOrders.ForEachAsync())
{
    try
    {
        Console.WriteLine($"Received: {order.Value.OrderId}");
        await order.CommitAsync();
    }
    catch
    {
        await order.NegativeAckAsync();
    }
}

```
手動コミットを使用する場合、`ForEachAsync()` は `IManualCommitMessage<T>` 型のオブジェクトを返します。
このオブジェクトは `.Value` プロパティで元のメッセージにアクセスでき、`.CommitAsync()` / `.NegativeAckAsync()` によって処理完了／失敗通知を制御します。
```csharp
public interface IManualCommitMessage<T>
{
    T Value { get; }
    Task CommitAsync();
    Task NegativeAckAsync();
}
```
この型は手動コミット指定時のみ返され、自動コミット時は T のままになります（ForEachAsync() の中で分岐）

## 4. POCO (Plain Old CLR Objects) の設計

### 4.1 基本定義
- シンプルなC#クラス: 特別な基底クラス不要
- 標準的なプロパティ: 一般的な.NET型のサポート


### 4.2 型のサポート

以下はPOCOで使用可能な主なデータ型の一覧です：

|データ型|説明|
---|---
int, long|整数型、KafkaではAvro/Protobuf経由でそのままマッピング可能
float, double|浮動小数点数
decimal|高精度数値。[DecimalPrecision]で精度指定可能
bool|真偽値
string|テキスト
DateTime, DateTimeOffset|日時型。Kafkaへの送信時にUTC変換処理が入る
Guid|一意識別子としてサポート
short|Kafkaでは int として扱われます。使用可能ですが、必要に応じて明示的なスキーマ変換を考慮してください。
char|Kafkaには直接の対応がなく、事実上非推奨です。1文字は string 型で表現することを推奨します。

#### Decimal型の精度指定
```csharp
[DecimalPrecision(precision: 18, scale: 4)]
public decimal Amount { get; set; }
```

#### DateTime/DateTimeOffset
```csharp
// DateTimeOffset推奨（タイムゾーン情報保持）
public DateTimeOffset TransactionTime { get; set; }

// または設定付きのDateTime
[DateTimeFormat(Format = "yyyy-MM-dd'T'HH:mm:ss.SSS", Locale = "en-US")]
public DateTime OrderDate { get; set; }
```
Kafkaにはタイムゾーンの概念がなく、すべての時刻はUTCとして扱われます。このため、プロデューサとコンシューマが同一のタイムゾーン前提を共有していることが重要です。このフレームワークは Kafka に不慣れな開発者でも安全に利用できるよう設計されています。DateTimeOffset を利用することで、開発者はタイムゾーン情報を保ったままアプリケーション開発ができ、Kafka との間の UTC 変換はフレームワークが吸収します。そのため、タイムゾーンを意識したビジネスロジックの記述が可能です。また、DateTime を使用しても、Kafka送信時には内部的に DateTimeOffset(Utc) に変換されるため、安全に運用可能です。このため、プロデューサとコンシューマが同一のタイムゾーン前提を共有していることが重要です。

このフレームワークは Kafka に不慣れな開発者でも安全に利用できるよう設計されています。
DateTimeOffset を利用することで、開発者はタイムゾーン情報を保ったままアプリケーション開発ができ、Kafka との間の UTC 変換はフレームワークが吸収します。
そのため、タイムゾーンを意識したビジネスロジックの記述が可能です。
#### null許容性
```csharp
// C#標準の ?修飾子を使用
public int? OptionalQuantity { get; set; }
```

#### 数値型のデフォルト値
```csharp
[DefaultValue(10)]
public int Quantity { get; set; }
```
非nullableな数値プロパティ（例：int, double など）は、C#の仕様により初期化されていない場合でも自動的に 0 や 0.0 などの既定値が適用されます。Kafka送信時もこれらの値がそのまま使用されます。特別な初期化が不要なケースでは [DefaultValue] 属性は省略可能です。

## 5. プロデュース操作
このセクションでは、Kafka にデータを送信（プロデュース）する際の基本的な操作方法とフレームワークが提供する抽象化手法について説明します。開発者は Kafka の複雑な設定を意識することなく、Entity Framework ライクな記述でストリームデータを扱うことができます。
### 5.1 プロデューサー (データ送信)
Kafkaのリアルタイム性を維持するため、本フレームワークでは AddAsync によって即時にKafkaへ送信が行われます。
Entity Frameworkのように SaveChangesAsync によってバッファを明示的にフラッシュする設計とは異なります。
このため、開発者は AddAsync を「送信操作」として扱うことを前提に実装してください。
```csharp
// 単一レコードのプロデュース
await context.Orders.AddAsync(new Order { OrderId = "123", Amount = 100 });

```
AddAsync を呼び出すことで、該当する Kafka トピックにイベントが送信されます。

複数件のバッファリング送信や並列制御も内部で吸収されます

### 5.3 トランザクション処理
Kafka は一部の操作に対して「プロデューサートランザクション」をサポートしています。ただし、ksqlDB 側ではトランザクション処理を前提としていないため、本フレームワークでは以下のような方針を採用します：

明示的なトランザクション API は提供しない（例：BeginTransaction / CommitTransaction）

Kafka における「Exactly Once Semantics (EOS)」をサポートする構成の場合でも、アプリケーション側では操作単位の idempotency を保証してください。

複数件の連続送信が必要な場合、アプリケーション側で逐次 AddAsync() を呼び出す実装で十分に高い信頼性を確保できます。

このフレームワークでは、Kafka のリアルタイムストリーム処理の思想に従い、永続化トランザクションよりも 即時性と可用性のバランスを優先しています。


## 6. エラー処理とデータ品質

### 6.1 エラー処理戦略
```csharp
// エラー処理ポリシーの設定
context.Options.DeserializationErrorPolicy = ErrorPolicy.Skip;

// エラーハンドリング付きストリーム処理
var processedOrders = context.Orders
    .OnError(ErrorAction.Skip)  // エラーレコードをスキップ
    .Map(order => ProcessOrder(order))
    .WithRetry(3);  // 失敗時に3回リトライ
```

### 6.2 デッドレターキュー
DLQ の設定は、エンティティ定義時に行います。以下のように modelBuilder.Entity<T>() の中で指定することで、対象のストリーム／テーブルごとに個別の DLQ を定義できます。
DLQ トピック名は省略可能で、省略された場合は "{EntityName}-dlq" の形式で自動生成されます。
```csharp
modelBuilder.Entity<Order>()
    .WithDeadLetterQueue(); 
    // トピック名は "orders-dlq" のように自動生成されます
```
DLQ を設定することで、変換や送信に失敗したデータを指定トピックへ退避可能です。

DLQ に送られたメッセージは後続で再処理やモニタリングの対象となります。

### 6.3 スキーマフォーマットについて

本フレームワークでは、Kafka のスキーマレジストリと連携するフォーマットとして Avro のみ をサポートしています。

JSON Schema は扱いません（理由：データサイズ・速度・互換性管理の観点から）

POCO から Avro スキーマは自動生成され、初回登録 or 更新時にレジストリへ登録されます

スキーマレジストリの互換性設定（BACKWARD, FULL など）に応じた開発を推奨します

## 7. テーブル管理操作（開発・検証用途）
本フレームワークでは、KSQL上に定義されるストリーム／テーブルの初期化・削除を、開発・検証用途に限り API として提供します。

```csharp
await context.EnsureTableCreatedAsync<HourlyStats>();
await context.DropTableAsync("hourly_stats");
```
注意:
これらの操作は 開発・CI/CD 環境での利用を前提 としており、本番環境での実行は推奨されません。
本番では modelBuilder による明示的な定義と、デプロイ時のDDL管理が基本となります。


## 8. リリース計画

### フェーズ1: 基盤構築 (v0.1-v0.3)
- トピックおよびストリーム定義 DSL
- POCO ベースのデータ定義とマッピング
- プロデュース / コンシューム操作の抽象化
- Dead Letter Queue 対応


### フェーズ2: 高度なストリーム処理 (v0.4-v0.6)
- LINQ ベースのウィンドウ・集約処理
- ストリーム / テーブルの明示的切り替え
- スキーマ進化対応の検討

### フェーズ3: 高度なデータ連携 (v0.7-v0.9)
- JOIN処理のDSL化（複合キー対応含む）
- テーブル定義と RocksDB 前提の読み取り最適化
- エラー通知・メトリクス・DLQ監視基盤との連携

### フェーズ4: エンタープライズ機能 (v1.0+)
- 分散トレーシングとメトリクス
- トランザクショナルメッセージング
- マルチクラスタサポート

## 9. アーキテクチャ概要
本フレームワークは、Kafka／ksqlDB 環境における Entity Framework ライクな操作を実現するために、以下の主要レイヤーと責務をもって構成されます。
### 9.1 レイヤー構成

```
+------------------------------+
| アプリケーション層           |
|------------------------------|
| - コンシューマ購読処理       |
| - LINQベースのDSL記述       |
| - POCO定義（エンティティ）   |
+------------------------------+
            ↓
+------------------------------+
| DSL定義／マッピング層        |
|------------------------------|
| - modelBuilder によるDSL定義 |
| - LINQの解釈とクエリ変換     |
| - ストリーム／テーブル区別   |
+------------------------------+
            ↓
+------------------------------+
| コード解析・モデル構築層     |
|------------------------------|
| - POCO構造の解析              |
| - Avroスキーマの自動生成     |
| - スキーマレジストリへの登録 |
| - トピック／DLQ名の解決       |
+------------------------------+
            ↓
+------------------------------+
| Kafka連携層（プロデュース／購読）|
|------------------------------|
| - Kafkaへの送信（AddAsync）   |
| - ForEachAsyncによる購読処理 |
| - DLQへのエラールーティング   |
+------------------------------+
            ↓
+------------------------------+
| KSQL操作層                   |
|------------------------------|
| - CREATE STREAM/TABLE生成    |
| - JOIN / AGGREGATE の発行    |
| - クエリ登録と監視           |
+------------------------------+
            ↓
+------------------------------+
| RocksDBキャッシュ層（KTable backing store） |
|------------------------------|
| - KTable/テーブル参照時にRocksDBでローカルキャッシュ
| - 参照クエリは極力RocksDB経由で応答（遅延低減・パフォーマンス最適化）
+------------------------------+

```
### 9.2 フォーマットポリシー

本フレームワークでは Kafka との通信において Avro のみを正式サポート しています。

JSON Schema はサポート対象外です（理由：サイズ効率・速度・互換性管理の明確性）。

スキーマは自動生成され、スキーマレジストリに登録されます。

### 9.3 補足

スキーマレジストリの接続設定や互換性ルール（BACKWARD 等）は別途構成で指定。

ストリーム／テーブルの定義、DLQ設定、LINQ DSL、プロデュース／購読などの責務が明確に分離されており、将来的な拡張やプラガブルアーキテクチャが可能です。

 #### RocksDBキャッシュ設計思想

 本フレームワークでは、KSQLのテーブル（KTable）参照時に、RocksDBをバックエンドストアとして自動利用し、参照クエリのパフォーマンスを最大化しています。

 - 通常の`.ToList()`や`.FirstOrDefault()`などの参照はRocksDBを経由し、ksqlDBやKafka本体への都度問い合わせを避けます。
 - RocksDB層はデフォルトで有効化。キャッシュサイズや永続化設定など詳細は将来的にFluent APIで拡張予定です。
 - この設計により、「大量データ参照でもレイテンシが大幅に低減」されます。
 - 詳細なパフォーマンス設計例はFAQ・運用ガイド参照。

10.4 DIコンテナと初期化タイミング

本フレームワークの利用においては、KafkaDbContext は DI コンテナを通じてスコープ管理されることを想定しています。

DbContext のインスタンスが解決されると、内部的に OnModelCreating() が呼ばれ、LINQベースの DSL 定義が構築・解析されます。

コンストラクタは軽量であり、DSL構文の構築のみを行います。

Kafkaやスキーマレジストリへの接続確認・リソース作成は、以下のように明示的に EnsureKafkaReadyAsync() を呼び出して実行します：

```
public class KafkaInitializer
{
    private readonly KafkaDbContext _context;

    public KafkaInitializer(KafkaDbContext context)
    {
        _context = context;
    }

    public async Task InitializeAsync()
    {
        await _context.EnsureKafkaReadyAsync();
    }
}

```
注意点：このタイミングで Schema Registry への通信や Kafka メタデータ取得処理が走るため、接続先が利用可能でない場合に例外が発生する可能性があります。

そのため、KafkaDbContext 自体は軽量な構築とし、重い外部接続処理は明示的な初期化メソッドに切り出しています。

EF と異なり Kafka OSS では「コードに基づいて Kafka 側の構造を生成する」ため、初期化を明示化することで、利用者の意図したタイミングでエラーを把握できる構造となっています。

この点を考慮し、Kafka 接続が不要な構文検証モードを別途用意することも検討されます（例：ValidateModelOnly=true オプション）。

Kafka接続不要な構文検証モードの価値

このモードには以下の利点があります：

CI/CD での構文検証：Kafka や Schema Registry が起動していない環境でも LINQ DSL や POCO の定義が妥当かを自動チェック可能。

Kafka インフラ未構築でも開発可能：先に LINQ や POCO を定義し、Kafka が準備される前に開発が進行可能。

安全な単体テスト：Kafka 接続なしでローカルテストが可能。

学習コスト低減：Kafka 環境のセットアップを待たずに、DSL定義の学習・試行錯誤が可能。。


## 11. Kafkaのcommit/DB commit・障害時の動作（DBエンジニア必読）

Kafkaのコンシューマアプリでは「オフセットcommit」と「DBのトランザクションcommit」は同じではありません。 特にDBエンジニア・テックリード層に多い勘違いとして、\*\*障害発生時には「前回commitしたオフセット」から“再度メッセージが流れてくる”\*\*という動作を理解しておく必要があります。

### サンプル：障害発生時の「重複実行」イメージ

```csharp
foreach (var msg in consumer.Consume())
{
    // 1. DBに書き込む
    db.Save(msg.Value); // 例：OrdersテーブルにINSERT

    // 2. Kafkaにオフセットcommit（"ここまで処理済み"を通知）
    consumer.Commit(msg);
}
```

#### ▼このときの「状態」例

| 処理        | DB     | Kafkaオフセット | 備考                       |
| --------- | ------ | ---------- | ------------------------ |
| 初回実行      | 書き込み済み | commit済み   | 1回だけでOK                  |
| commit前障害 | 書き込み済み | commit前    | **再起動後、同じmsgを再実行（DB重複）** |
| commit後障害 | 書き込み済み | commit済み   | 以降は次のmsgから処理             |

#### 【冪等化例：重複反映を防ぐパターン】

```csharp
foreach (var msg in consumer.Consume())
{
    if (!db.Exists(msg.Key))
    {
        db.Save(msg.Value);
    }
    consumer.Commit(msg);
}
```

- こうすることで、**再実行されてもDBは一意に保たれる**（冪等性担保）

### 解説

- Kafkaのcommitは「オフセット管理」でありDBのcommitとは意味が違う
- commit前の副作用は何度も再実行される前提で設計する
- 特にDB系テックリードは「一意反映」と誤認しやすいので**冪等設計必須**
- こうした違いを理解せずに設計すると「二重反映」「消えたデータ」問題に直結

