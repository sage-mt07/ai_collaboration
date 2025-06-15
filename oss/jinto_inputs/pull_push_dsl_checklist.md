# Pull/Push対応DSL設計・適用チェックリスト（迅人・鏡花向け）

本チェックリストは、Kafka + ksqlDB DSL において Push/Pull Query の使い分けが正しく実装・テストされているかを確認するための観点を整理したものである。迅人はテスト設計・生成時に、鏡花はレビュー時に本項目を参照すること。

---

## ✅ DSLレイヤ：設計観点

### 1. 実行メソッドにより Push / Pull の意図が明確に分離されているか？

- `ToList()` / `ToListAsync()` → Pull Query（EMIT CHANGESなし）
- `ForEachAsync()` / `await foreach` → Push Query（EMIT CHANGES付き）

### 2. `ToKsql()` に `isPullQuery` フラグが存在し、呼び出し元で適切に指定されているか？

- `ToList()` からの呼び出し → `isPullQuery: true`
- `ForEachAsync()` からの呼び出し → `isPullQuery: false`

### 3. `ToKsql()` 内の EMIT句の制御が条件分岐で実装されているか？

```csharp
if (!isPullQuery && 条件)
{
    query.Append(" EMIT CHANGES");
}
```

### 4. Pull対象が KTable のみであることを将来的に制約可能な構造になっているか？

（※ 現時点では制限しないが、今後のスキーマ識別のためにメタデータ設計を意識）

---

## ✅ 実装レイヤ：テスト設計・生成観点（迅人向け）

### 5. Pull Query のテストケースが存在するか？

- `.ToList()` 実行 → EMIT CHANGES を含まないクエリを生成しているか？
- `.ToList()` 実行 → Consumer からの取得が 1 回限りで完結しているか？

### 6. Push Query のテストケースが存在するか？

- `.ForEachAsync()` 実行 → EMIT CHANGES を含むクエリが生成されているか？
- 逐次コールバックが呼ばれていることを確認できるか？（モック／トレース）

### 7. `ToKsql()` の出力を直接検証するテストが存在するか？

- `ToKsql(true)` で EMIT句がないこと
- `ToKsql(false)` で EMIT句が含まれていること

---

## ✅ レビュー観点（鏡花向け）

### 8. Push/Pull の責務がコード上から自然に読み取れるか？

- API命名・構文・ドキュメントで意図が明確になっているか？

### 9. Push操作がPull基盤で実装されていないか？（過去の誤実装確認）

- `ForEachAsync()` で `ToListAsync()` を使用していないか？

### 10. Pull/Pushの切り替えを曖昧にする条件分岐が存在しないか？

- LINQ解析レイヤでPush/Pullが動的に切り替わると設計が破綻しやすい。

---

## 備考

- Pull/Pull の明確な切り替えは、Kafka/ksqlDBの構造的制約に基づくものである
- EMIT CHANGES の有無だけでなく、**構文・使用対象（Stream/Table）** の適合性も将来的に考慮が必要

---

以上

