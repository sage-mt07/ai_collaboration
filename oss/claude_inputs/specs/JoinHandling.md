# JoinHandling.md

## 概要
Ksql.EntityFrameworkCore での LINQ Join を KSQL JOIN へ変換するための仕様文書です。

本ドキュメントは、`KsqlJoinBuilder` を中心とする JOIN 機能群の設計・責務・制約・使用例を記述します。

---

## 対象クラス群

| クラス名                    | 役割                                      |
|-----------------------------|-------------------------------------------|
| `KsqlJoinBuilder`           | LINQ `join` 式の解析と KSQL JOIN句生成     |
| `KsqlConditionBuilder`      | `on ... equals ...` の式解析・文字列出力  |
| `StreamTableInferenceAnalyzer` | Stream/Table の種別推論                  |
| `InferenceResult`           | 推論結果の保持                           |

---

## 対応LINQ構文（例）

```csharp
from o in Orders
join c in Customers
on o.CustomerId equals c.Id
select new { o.Id, c.Name }
```

---

## KSQL変換例

```sql
SELECT o.Id, c.Name
FROM Orders o
INNER JOIN Customers c
  ON o.CustomerId = c.Id
EMIT CHANGES;
```

---

## 設計上の制約と前提

- JOIN元は基本的に `STREAM` であること（KSQLの制約に従う）
- `LEFT JOIN` などの拡張は将来的に対応予定（現状は `INNER JOIN` のみ）
- `ON` 句は単一または複数の `equals` 式（複合キーもサポート）
- 対象のエンティティはすべて `KsqlModelBuilder` にて `.Stream()` または `.Table()` として登録済みである必要がある

---

### 複合キーの対応例

```csharp
join b in B
on new { a.Id, a.Type } equals new { b.Id, b.Type }
```

```sql
... ON a.Id = b.Id AND a.Type = b.Type
```

内部的には `KsqlConditionBuilder` にて複数プロパティの比較を組み合わせて `AND` 条件として出力します。

---

## 今後の拡張ポイント

- `LEFT JOIN`, `FULL OUTER JOIN` などの構文対応
- `ON` 句に複雑な論理式を許容する式木の強化
- Join対象の Avro スキーマ整合性チェックの自動化
- Join実行時の状態保存方式（KTable参照 vs Stream-Stream）

