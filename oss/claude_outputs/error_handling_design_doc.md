# 第6章 エラー処理設計

## 6.1 DSLチェーン構文による例外制御

### 6.1.1 概要

Kafkaコンシューマにおける業務ロジックの例外制御を、LINQ構文の美しさを保ちながら実現するDSLチェーン構文を提供します。この構文は「安心してKafkaを使える」という本OSSの根本的価値を担保する核となる機能です。

DLQはシステム全体で1つのトピック（`dead.letter.queue`）を使用し、自動処理により個別設定は不要です。

### 6.1.2 基本構文

```csharp
context.Orders
    .OnError(ErrorAction.DLQ)
    .Map(order => ProcessOrder(order))
    .WithRetry(3);
```

### 6.1.3 DSL意味論対応表

| DSL | 意味 | 実行位置 | 型安全性 |
|-----|------|----------|----------|
| `.OnError(ErrorAction.Skip)` | エラーレコードをスキップして処理継続 | Map の前に設定 | ErrorAction列挙型による保証 |
| `.OnError(ErrorAction.Retry)` | 指定回数リトライ実行 | Map の前に設定 | WithRetry()と組み合わせ |
| `.OnError(ErrorAction.DLQ)` | Dead Letter Queueに自動送信 | Map の前に設定 | 内部DlqProducerによる処理 |
| `.Map(...)` | POCOを業務ロジックに渡す | Kafkaから受信後 | Func&lt;T, TResult&gt;による型変換 |
| `.WithRetry(3)` | Retry指定時の最大試行回数 | ErrorAction.Retry時に有効 | 指数バックオフによる待機 |

### 6.1.4 実行フロー

1. **事前設定フェーズ**
   - `OnError()`: 例外処理方針の設定
   - `WithRetry()`: リトライ回数・間隔の設定

2. **実行フェーズ**
   - `Map()`: 各要素に対して業務ロジックを適用
   - エラーハンドリング: 設定に基づく例外処理実行
   - DLQ自動送信: ErrorAction.DLQ時の自動処理

3. **結果取得フェーズ**
   - `GetResults()`: 処理済み要素の取得
   - `ForEachAsync()`: 非同期列挙処理

### 6.1.5 DLQ設計原則

**基本方針**:
- DLQトピックはシステム全体で1つ: `dead.letter.queue`
- 保存形式はAvroまたはbyte[]（バイナリ形式）
- JSON化は行わず、再処理を主眼に置いた構造
- 個別のWithDeadLetterHandler()は使用しない

**DlqEnvelope構造**:
```csharp
public class DlqEnvelope
{
    public string Topic { get; set; }              // 元のトピック名
    public int Partition { get; set; }             // 元のパーティション
    public long Offset { get; set; }               // 元のオフセット
    public byte[] AvroPayload { get; set; }        // 元のメッセージ本体（バイナリ）
    public string ExceptionType { get; set; }      // 例外の型名
    public string ExceptionMessage { get; set; }   // 例外メッセージ
    public string? StackTrace { get; set; }        // スタックトレース
    public DateTime Timestamp { get; set; }        // DLQ送信時刻
    public int RetryCount { get; set; }            // リトライ回数
    public string? CorrelationId { get; set; }     // 相関ID
}
```

### 6.1.6 非同期制御とエラーハンドリング

```csharp
// 非同期版
var result = await context.Orders
    .OnError(ErrorAction.DLQ)
    .Map(async order => await ProcessOrderAsync(order))
    .WithRetry(3);

// 同期版
var result = context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => ProcessOrder(order))
    .WithRetry(2);
```

内部実装では以下の制御を実現：

- **ErrorHandlingContext**: アイテム毎の独立したエラー処理状態管理
- **自動DLQ送信**: ErrorAction.DLQ時の透明な送信処理
- **リトライ制御**: ErrorAction.Retry時の指数バックオフ実装

### 6.1.7 エラーアクション詳細

#### ErrorAction.Skip
```csharp
// エラーレコードをスキップして処理継続
context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => {
        if (order.Quantity <= 0) 
            throw new InvalidOperationException("無効な数量");
        return ProcessOrder(order);
    });
// 無効な注文はスキップされ、有効な注文のみ処理される
```

#### ErrorAction.Retry
```csharp
// 指定回数リトライ実行
context.Orders
    .OnError(ErrorAction.Retry)
    .WithRetry(3, TimeSpan.FromSeconds(2))  // 3回リトライ、2秒間隔
    .Map(order => ProcessOrder(order));
// 失敗した処理を最大3回まで再試行、最終失敗時はスキップ
```

#### ErrorAction.DLQ
```csharp
// Dead Letter Queueに自動送信
context.Orders
    .OnError(ErrorAction.DLQ)
    .Map(order => ProcessOrder(order));
// 処理失敗したレコードは自動的にシステム共通のDLQに送信される
// 個別のハンドラー設定は不要
```

### 6.1.8 アーキテクチャ構成

**レイヤー分離**:
```
KsqlDsl.EventSet<T>              // DSLチェーン（OnError, Map, WithRetry）
    ↓
KsqlDsl.Messaging.Internal       // ErrorHandlingContext（実行時状態管理）
    ↓
KsqlDsl.Messaging.Contracts      // IErrorSink（抽象化）
    ↓
KsqlDsl.Messaging.Producers      // DlqProducer（DLQ送信実装）
    ↓
KsqlDsl.Messaging.Models         // DlqEnvelope（DLQメッセージ構造）
```

**責務分離**:
- **EventSet**: 宣言的DSL提供
- **ErrorHandlingContext**: 実行時エラー状態管理
- **DlqProducer**: DLQ送信の実装
- **IErrorSink**: 将来拡張・Mock対応の抽象化

### 6.1.9 実用パターン

```csharp
// パターン1: 高速処理（エラーは単純スキップ）
context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => ProcessOrder(order));

// パターン2: 信頼性重視（リトライ後スキップ）
context.Orders
    .OnError(ErrorAction.Retry)
    .WithRetry(3)
    .Map(order => ProcessOrder(order));

// パターン3: 完全性重視（DLQで全データ保持）
context.Orders
    .OnError(ErrorAction.DLQ)
    .Map(order => ProcessOrder(order));

// パターン4: 非同期ストリーミング処理
await context.Orders
    .OnError(ErrorAction.DLQ)
    .ForEachAsync(async order => {
        await ProcessOrderAsync(order);
    });
```

### 6.1.10 DI統合パターン

```csharp
// Program.cs または Startup.cs
services.AddSingleton<IErrorSink, DlqProducer>();
services.Configure<KsqlDslOptions>(options => {
    options.DlqTopicName = "dead.letter.queue";
});

// 使用時
public class OrderService
{
    private readonly KafkaContext _context;
    
    public OrderService(KafkaContext context)
    {
        _context = context; // DlqProducerが自動注入される
    }
    
    public async Task ProcessOrdersAsync()
    {
        await _context.Orders
            .OnError(ErrorAction.DLQ)
            .Map(ProcessOrder)
            .ForEachAsync(result => Console.WriteLine(result));
    }
}
```

### 6.1.11 設計思想とバイナリ保存の重要性

**哲学的設計**: この DSL は単なる例外ハンドラではなく、**Kafkaと人間の橋渡し**として機能します。

**バイナリ保存の意義**:
- **再処理可能性**: JSON化せずAvroバイナリで保存することで、スキーマ変更に対応した再処理が可能
- **データ完全性**: 元のメッセージを完全に復元でき、情報の欠損がない
- **拡張性**: 将来的なスキーマ進化に対応可能

**運用における価値**:
- DLQは「可視化のため」ではなく「安全な失敗データ退避装置」として設計
- 分析は別の手段で行い、DLQにロジックを持たせない
- 責任の最小化と復旧性の最大化を実現

この設計により、本OSSは「安心してKafkaを使える」環境を提供し、開発者が業務ロジックに集中できる基盤を構築します。

### 6.1.4 実行フロー

1. **事前設定フェーズ**
   - `OnError()`: 例外処理方針の設定
   - `WithRetry()`: リトライ回数の設定

2. **実行フェーズ**
   - `Map()`: 各要素に対して業務ロジックを適用
   - リトライ機構: 失敗時の自動再試行
   - 例外ハンドリング: 設定に基づく例外処理

3. **結果取得フェーズ**
   - `GetResults()`: 処理済み要素の取得

### 6.1.5 非同期制御とtry/catch/await

```csharp
// 非同期版
var result = await context.Orders
    .OnError(ErrorAction.Skip)
    .Map(async order => await ProcessOrderAsync(order))
    .WithRetry(3);

// 同期版
var result = context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => ProcessOrder(order))
    .WithRetry(3);
```

内部実装では以下の非同期制御を実現：

- **await による非同期実行**: Map内の業務ロジックが非同期の場合、適切にawaitされる
- **try/catch による例外捕捉**: 各要素の処理で発生した例外を個別に処理
- **Task.Delay による待機制御**: リトライ時の指数バックオフ実装

### 6.1.6 型安全性の担保

```csharp
// 型変換の例
EventSet<Order> orders = context.Orders;                    // Order型
EventSet<string> processed = orders.Map(o => o.ToString()); // string型に変換
EventSet<ProcessResult> results = orders.Map(ProcessOrder); // ProcessResult型に変換
```

- **ジェネリクス活用**: `EventSet<T>`により入力型を保持
- **Func デリゲート**: `Func<T, TResult>`により変換関数の型安全性を保証
- **コンパイル時型検査**: 不正な型変換はコンパイルエラーとして検出

### 6.1.7 エラーアクション詳細

#### ErrorAction.Skip
```csharp
// エラーレコードをスキップして処理継続
context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => {
        if (order.Quantity <= 0) 
            throw new InvalidOperationException("無効な数量");
        return ProcessOrder(order);
    });
// 無効な注文はスキップされ、有効な注文のみ処理される
```

#### ErrorAction.Retry
```csharp
// 指定回数リトライ実行
context.Orders
    .OnError(ErrorAction.Retry)
    .WithRetry(3)  // 最大3回リトライ
    .Map(order => ProcessOrder(order));
// 失敗した処理を最大3回まで再試行
```

#### ErrorAction.DeadLetter
```csharp
// Dead Letter Queueに送信
context.Orders
    .OnError(ErrorAction.DeadLetter)
    .WithDeadLetterHandler(async (order, ex) => {
        // DLQトピックに送信
        await kafkaProducer.ProduceAsync("orders-dlq", order);
        logger.LogError(ex, "Order {OrderId} sent to DLQ", order.Id);
    })
    .Map(order => ProcessOrder(order));
// 処理失敗したレコードは自動的にDLQに送信される
```

### 6.1.8 リトライ機構

```csharp
// 指数バックオフによるリトライ
context.Orders
    .WithRetry(3)  // 最大3回リトライ
    .Map(order => ProcessOrder(order));
```

**リトライ間隔**:
- 1回目の失敗後: 100ms待機
- 2回目の失敗後: 200ms待機  
- 3回目の失敗後: 400ms待機
- 4回目で最終的に失敗

### 6.1.9 組み合わせパターン

```csharp
// 推奨パターン1: エラースキップ（高速処理）
context.Orders
    .OnError(ErrorAction.Skip)
    .Map(order => ProcessOrder(order));

// 推奨パターン2: リトライ戦略（信頼性重視）
context.Orders
    .OnError(ErrorAction.Retry)
    .WithRetry(3)
    .Map(order => ProcessOrder(order));

// 推奨パターン3: DLQ戦略（完全性重視）
context.Orders
    .OnError(ErrorAction.DeadLetter)
    .WithDeadLetterHandler(async (order, ex) => {
        await SendToDLQ(order, ex);
    })
    .Map(order => ProcessOrder(order));

// 複合パターン: リトライ後DLQ送信
context.Orders
    .OnError(ErrorAction.Retry)
    .WithRetry(2)
    .OnError(ErrorAction.DeadLetter) // リトライ失敗後にDLQ
    .WithDeadLetterHandler(async (order, ex) => {
        await SendToDLQ(order, ex);
    })
    .Map(order => ProcessOrder(order));
```

### 6.1.10 哲学的設計思想

この DSL は単なる例外ハンドラではなく、**Kafkaと人間の橋渡し**として機能します：

- **Kafkaの世界**: 例外処理のない、純粋なストリーミングデータ
- **人間の世界**: 業務ロジックでの例外処理が必要な現実
- **DSLの役割**: 両者を美しく繋ぐインターフェース

LINQ構文の美しさを保ちながら、現場での運用実装を直接的に支える哲学的機能として設計されています。

### 6.1.11 パフォーマンス考慮事項

- **メモリ効率**: 中間結果をストリーミング処理で最小化
- **非同期最適化**: ConfigureAwait(false)による文脈切り替え回避
- **例外コスト**: try/catch のオーバーヘッドを最小限に抑制
- **リトライ最適化**: 指数バックオフによる適切な負荷分散

この設計により、本OSSは「安心してKafkaを使える」環境を提供し、開発者が業務ロジックに集中できる基盤を構築します。