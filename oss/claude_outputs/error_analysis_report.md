# Kafka.Ksql.Linq コンパイルエラー解析レポート

## 🔍 エラー分析結果

### 主要なエラーカテゴリ

#### 1. インターフェース実装不足（CS0535）
- `DlqProducer` が `IErrorSink.HandleErrorAsync<T>` を実装していない
- `JoinableEntitySet<T>` が複数のインターフェースメンバーを実装していない
- `WindowedEntitySet<T>` が複数のインターフェースメンバーを実装していない

#### 2. 名前空間の型衝突（CS0118）
- `Exception` が名前空間として認識される問題（複数箇所）

#### 3. メソッドオーバーライド問題（CS0115）
- `SendEntityAsync` メソッドのオーバーライドエラー（複数クラス）
- `ExecuteQueryAsync` メソッドのオーバーライドエラー

#### 4. 重複定義エラー（CS0102, CS0111）
- `ErrorHandlingPolicy` の複数プロパティが重複定義
- `CircuitBreakerHandler` のコンストラクタ重複

#### 5. 型制約エラー（CS0452）
- ジェネリック型パラメータ `T` の参照型制約エラー

#### 6. その他の構文エラー
- `throw` ステートメントの不適切な使用（CS0156）
- 未定義プロパティアクセス（CS0117, CS1061）
- あいまいな参照（CS0229）

## 🛠️ 解決策

### 1. DlqProducer の修正

```csharp
// src/Messaging/Producers/DlqProducer.cs
public class DlqProducer : IErrorSink, IDisposable
{
    // 既存のコード...

    // ✅ 追加：ジェネリック版HandleErrorAsyncの実装
    public async Task HandleErrorAsync<T>(T originalMessage, Exception exception, 
        KafkaMessageContext? context = null, CancellationToken cancellationToken = default) 
        where T : class
    {
        // 既存の実装をそのまま使用
        await HandleErrorAsync((object)originalMessage, exception, context, cancellationToken);
    }

    // 既存の非ジェネリック版を保持
    private async Task HandleErrorAsync(object originalMessage, Exception exception,
        KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
    {
        // 既存の実装
    }
}
```

### 2. Exception 名前空間衝突の修正

```csharp
// 各ファイルで System.Exception を明示的に使用
using SystemException = System.Exception;

// または using ディレクティブを追加
using System;

// そして Exception の代わりに System.Exception を使用
public async Task HandleErrorAsync<T>(T originalMessage, System.Exception exception, 
    KafkaMessageContext? context = null, CancellationToken cancellationToken = default)
```

### 3. ErrorHandlingPolicy の重複定義修正

```csharp
// src/Core/Abstractions/ErrorHandlingPolicy.cs
public class ErrorHandlingPolicy
{
    // ✅ 基本プロパティ（重複削除）
    public ErrorAction Action { get; set; } = ErrorAction.Skip;
    public int RetryCount { get; set; } = 3;
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromSeconds(1);
    
    // ✅ 拡張プロパティ（1つずつ定義）
    public Func<ErrorContext, object, bool>? CustomHandler { get; set; }
    public Predicate<System.Exception>? RetryCondition { get; set; }
    public Action<ErrorMetrics>? MetricsCallback { get; set; }
    public Func<ErrorContext, string>? AdditionalLogInfo { get; set; }
    public Func<int, TimeSpan>? DynamicRetryInterval { get; set; }

    // ✅ 静的ファクトリメソッド
    public static ErrorHandlingPolicy ExponentialBackoff(int maxRetries = 3, TimeSpan baseInterval = default)
    {
        var interval = baseInterval == default ? TimeSpan.FromSeconds(1) : baseInterval;
        return new ErrorHandlingPolicy
        {
            Action = ErrorAction.Retry,
            RetryCount = maxRetries,
            DynamicRetryInterval = attempt => TimeSpan.FromMilliseconds(
                interval.TotalMilliseconds * Math.Pow(2, attempt - 1))
        };
    }

    public static ErrorHandlingPolicy CircuitBreaker(int failureThreshold = 5, TimeSpan recoveryInterval = default)
    {
        var recovery = recoveryInterval == default ? TimeSpan.FromMinutes(1) : recoveryInterval;
        return new ErrorHandlingPolicy
        {
            Action = ErrorAction.Skip,
            CustomHandler = new CircuitBreakerHandler(failureThreshold, recovery).Handle
        };
    }
}
```

### 4. KsqlDslOptions の DlqTopicName プロパティ追加

```csharp
// src/Configuration/KsqlDslOptions.cs に追加
public class KsqlDslOptions
{
    // 既存のプロパティ...
    
    // ✅ 追加：DLQトピック名
    public string DlqTopicName { get; set; } = "dead.letter.queue";
}
```

### 5. EventSet の SendEntityAsync メソッド定義

```csharp
// src/EventSet.cs または基底クラスに追加
public abstract class EventSet<T> where T : class
{
    // ✅ 抽象メソッド定義
    protected abstract Task SendEntityAsync(T entity, CancellationToken cancellationToken);
    
    // 既存の実装...
}
```

### 6. JoinableEntitySet の完全実装

```csharp
// src/Query/Linq/JoinableEntitySet.cs
public class JoinableEntitySet<T> : EventSet<T>, IJoinableEntitySet<T> where T : class
{
    private readonly IEntitySet<T> _baseEntitySet;

    public JoinableEntitySet(IEntitySet<T> baseEntitySet) : base(baseEntitySet.GetContext(), baseEntitySet.GetEntityModel())
    {
        _baseEntitySet = baseEntitySet;
    }

    // ✅ 必須メソッドの実装
    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
    {
        await _baseEntitySet.AddAsync(entity, cancellationToken);
    }

    public async Task<List<T>> ToListAsync(CancellationToken cancellationToken = default)
    {
        return await _baseEntitySet.ToListAsync(cancellationToken);
    }

    public async Task ForEachAsync(Func<T, Task> action, TimeSpan timeout = default, CancellationToken cancellationToken = default)
    {
        await _baseEntitySet.ForEachAsync(action, timeout, cancellationToken);
    }

    public string GetTopicName() => _baseEntitySet.GetTopicName();
    public EntityModel GetEntityModel() => _baseEntitySet.GetEntityModel();
    public IKafkaContext GetContext() => _baseEntitySet.GetContext();

    public async IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        await foreach (var item in _baseEntitySet.WithCancellation(cancellationToken))
        {
            yield return item;
        }
    }

    // JOIN機能の実装
    public IJoinResult<T, TInner> Join<TInner, TKey>(
        IEntitySet<TInner> inner,
        Expression<Func<T, TKey>> outerKeySelector,
        Expression<Func<TInner, TKey>> innerKeySelector) where TInner : class
    {
        return new JoinResult<T, TInner>(this, inner, outerKeySelector, innerKeySelector);
    }
}
```

### 7. ErrorHandlingContext の throw 修正

```csharp
// src/Messaging/Internal/ErrorHandlingContext.cs
public class ErrorHandlingContext
{
    public async Task<bool> HandleErrorAsync<T>(T originalMessage, System.Exception exception,
        KafkaMessageContext? context = null) where T : class
    {
        // ✅ try-catch内でthrowを使用
        try
        {
            switch (ErrorAction)
            {
                case ErrorAction.Retry:
                    if (CurrentAttempt <= RetryCount)
                    {
                        if (CurrentAttempt < RetryCount)
                        {
                            await Task.Delay(RetryInterval);
                            throw exception; // ✅ 明示的に例外を指定
                        }
                    }
                    return false;

                case ErrorAction.Skip:
                    return false;

                case ErrorAction.DLQ:
                    if (ErrorSink != null)
                    {
                        await ErrorSink.HandleErrorAsync(originalMessage, exception, context);
                    }
                    return false;

                default:
                    throw new InvalidOperationException($"Unknown error action: {ErrorAction}");
            }
        }
        catch (System.Exception ex) when (ex != exception)
        {
            // 新しい例外の場合の処理
            throw;
        }
    }
}
```

## 📋 修正作業の優先順位

1. **高優先度**：インターフェース実装不足の解決
2. **中優先度**：重複定義エラーの解決
3. **低優先度**：名前空間衝突の解決

## 🔄 修正後の検証手順

1. コンパイルエラーの解消確認
2. 単体テストの実行
3. 統合テストの実行
4. ドキュメントとの整合性確認

この修正により、POCO属性主導型のKafka.Ksql.Linqライブラリが正常にコンパイルできるようになります。