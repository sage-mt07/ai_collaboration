# Task: JoinConditionEnhancement

## Title
KsqlConditionBuilder - 複合キー対応の実装

## Objective
Enable support for composite key join expressions in the form of anonymous types using LINQ.

Example:
```csharp
join b in B
on new { a.Id, a.Type } equals new { b.Id, b.Type }
```

Expected KSQL output:
```sql
... ON a.Id = b.Id AND a.Type = b.Type
```

## Requirements

- Enhance `KsqlConditionBuilder` to support `BinaryExpression` where `.Left` and `.Right` are `NewExpression`.
- Each element in the anonymous object should be compared with `=`, and joined using `AND`.
- Preserve compatibility with single-key join (`x.Id equals y.Id`).
- Add unit tests to validate composite key join transformation using representative LINQ expressions.
- Ensure that nested `ExpressionVisitor` handling is clean and reusable.

## Target Files

- `src/Ksql/KsqlConditionBuilder.cs`
- `tests/ksql_join_builder_tests.cs` (or equivalent test file for condition generation)

## Notes

- This task is aligned with JoinHandling.md design document.
- Consider potential reuse for `WHERE` clause in the future.
