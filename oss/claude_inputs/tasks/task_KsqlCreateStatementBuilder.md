# task_KsqlCreateStatementBuilder.md

## 🎯 Goal

Implement `KsqlCreateStatementBuilder` that generates `CREATE STREAM` or `CREATE TABLE` KSQL DDL statements based on metadata.

## 📘 Requirements

- Accept entity type and mapping context
- Use metadata or attributes to distinguish STREAM vs TABLE
- Generate KSQL DDL like:

```sql
CREATE STREAM Orders (Id INT, Amount DECIMAL) WITH (...);
CREATE TABLE ProductCatalog (ProductId INT, Price DECIMAL) WITH (...);
```

- Support optional WITH clause config (topic name, key format, value format, partitions, replicas, etc.)

## 🧩 Inputs

- POCO class definitions (C#)
- Type metadata (Stream vs Table) — e.g., via attribute or builder registration
- Property definitions (name, type, nullable)

## 🔧 Output

- Static method:

```csharp
public static string BuildCreateStatement(Type entityType, StreamTableType type, KsqlWithOptions options)
```

## ✅ Expected

- Correct keyword based on type (STREAM or TABLE)
- Valid column list inferred from class structure
- WITH clause included if options are present

## 🧪 Tests

- `CreateStreamStatement_Should_GenerateValidKsql()`
- `CreateTableStatement_WithOptions_Should_GenerateKsqlWithClause()`
- Invalid type should raise error

📂 Output file: `KsqlCreateStatementBuilder.cs`
