# Task: Implement `KsqlHavingBuilder`

## 🎯 Objective

Implement a new builder class `KsqlHavingBuilder` that can translate LINQ aggregate expressions into KSQL `HAVING` clauses.

This builder should be consistent with the structure and style of the existing builders like `KsqlConditionBuilder` and `KsqlAggregateBuilder`.

---

## ⚠️ Context Size Notice

This task may include detailed specifications and test cases.  
If the content appears too long or if you lose track of the initial requirements, please ask for a context refresh or clarification.  
Prioritize processing the provided specification file and test case below.

---

## 🧾 Input Example

```csharp
Expression<Func<IGrouping<string, Order>, bool>> expr = 
    g => g.Sum(x => x.Amount) > 1000;
```

---

## 🟢 Expected KSQL Output

```sql
HAVING SUM(Amount) > 1000
```

---

## 🧱 Requirements

- Class name: `KsqlHavingBuilder`
- Namespace: same as `KsqlAggregateBuilder`
- Input type: `Expression`
- Output type: `string` (KSQL clause)
- Target: Aggregate comparison expressions (e.g., `Sum`, `Count`, `Max`, `Min`)
- Avoid reflection or runtime evaluation
- All logic must be based on expression tree traversal

---

## 📚 References

- See: `KsqlAggregateBuilder.cs`, `KsqlConditionBuilder.cs` for builder structure
- Use: `KsqlTranslationTests.cs` for testing style and expected assertions

---

## 🧪 Testing

Add a test case to match the following pattern:

```csharp
Expression<Func<IGrouping<string, Order>, bool>> expr = 
    g => g.Sum(x => x.Amount) > 1000;

var result = KsqlHavingBuilder.Build(expr.Body);

Assert.Equal("HAVING SUM(Amount) > 1000", result);
```

---

## 📂 Output Placement

Please output your result to:

```
/claude_outputs/KsqlHavingBuilder.cs
/claude_outputs/KsqlHavingBuilderTests.cs
/claude_outputs/log_KsqlHavingBuilder.md
```

The log file should summarize:
- Purpose and input
- LINQ vs expected KSQL
- Test summary
- Note that human review is not yet done

---
