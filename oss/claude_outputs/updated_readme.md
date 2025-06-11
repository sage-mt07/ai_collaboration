# KsqlDsl

A C#-based DSL for generating KSQL queries from LINQ expression trees.  
Inspired by Entity Framework, tailored for Apache Kafka + ksqlDB integration.

---

## 🌟 Project Purpose

This library allows developers to:

- Use familiar C# expression trees to construct KSQL queries
- Abstract away the syntax complexity of KSQL
- Support `JOIN`, `WINDOW`, `GROUP BY`, `HAVING`, and other core KSQL clauses
- Focus on business logic, not query syntax

---

## 🚀 Quick Start

### Installation
```bash
dotnet add package KsqlDsl
```

### Basic Usage
```csharp
// Define your entity
public class OrderEntity
{
    public int OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
    
    [KafkaIgnore]
    public DateTime ProcessedAt { get; set; } // Excluded from schema
}

// Generate KSQL CREATE statement
var createStatement = KsqlCreateStatementBuilder.BuildCreateStatement(
    typeof(OrderEntity), 
    StreamTableType.Stream
);
// Result: "CREATE STREAM OrderEntity (OrderId INT, CustomerId VARCHAR, Amount DECIMAL)"

// Build LINQ → KSQL queries
Expression<Func<OrderEntity, bool>> whereExpr = o => o.Amount > 1000;
var whereClause = new KsqlConditionBuilder().Build(whereExpr.Body);
// Result: "WHERE (Amount > 1000)"
```

---

## 🏷️ Schema Customization

### KafkaIgnore Attribute
Exclude properties from Kafka schema generation and KSQL CREATE statements:

```csharp
public class OrderEntity
{
    // Included in schema
    public int OrderId { get; set; }
    public string CustomerId { get; set; }
    public decimal Amount { get; set; }
    
    // Excluded from schema
    [KafkaIgnore(Reason = "Internal processing timestamp")]
    public DateTime InternalTimestamp { get; set; }
    
    [KafkaIgnore(Reason = "Debug information, not for production")]
    public string DebugTrace { get; set; }
}
```

**Generated KSQL:**
```sql
CREATE STREAM OrderEntity (OrderId INT, CustomerId VARCHAR, Amount DECIMAL)
-- InternalTimestamp and DebugTrace are excluded
```

### Custom Type Mapping
```csharp
public class ProductEntity
{
    public int ProductId { get; set; }
    
    [DecimalPrecision(18, 4)]
    public decimal Price { get; set; }  // → DECIMAL(18, 4)
    
    public DateTime CreatedAt { get; set; }  // → TIMESTAMP
}
```

---

## 📋 Advanced Features

### Window Operations
```csharp
// Tumbling Window with advanced options
Expression<Func<ITumblingWindow>> windowExpr = () => 
    Window.TumblingWindow()
        .Size(TimeSpan.FromMinutes(5))
        .Retention(TimeSpan.FromHours(2))
        .GracePeriod(TimeSpan.FromSeconds(10))
        .EmitFinal();

var windowClause = new KsqlWindowBuilder().Build(windowExpr.Body);
// Result: "WINDOW TUMBLING (SIZE 5 MINUTES, RETENTION 2 HOURS, GRACE PERIOD 10 SECONDS) EMIT FINAL"
```

### JOIN Queries
```csharp
// Composite key JOIN with conditions
var joinBuilder = new KsqlJoinBuilder();
Expression<Func<OrderEntity, CustomerEntity, bool>> joinExpr = 
    (o, c) => new { o.CustomerId, o.Region } == new { c.CustomerId, c.Region } && c.IsActive;

var joinClause = joinBuilder.Build(joinExpr.Body);
// Result: "JOIN CustomerEntity c ON (o.CustomerId = c.CustomerId AND o.Region = c.Region) AND (c.IsActive = true)"
```

### Aggregate Functions with HAVING
```csharp
// Complex aggregation with HAVING clause
Expression<Func<IGrouping<string, OrderEntity>, object>> selectExpr = 
    g => new { 
        TotalAmount = g.Sum(x => x.Amount),
        OrderCount = g.Count(),
        MaxAmount = g.Max(x => x.Amount)
    };

Expression<Func<IGrouping<string, OrderEntity>, bool>> havingExpr = 
    g => g.Sum(x => x.Amount) > 10000 && g.Count() >= 5;

var selectClause = KsqlAggregateBuilder.Build(selectExpr.Body);
var havingClause = new KsqlHavingBuilder().Build(havingExpr.Body);

// Results:
// SELECT SUM(Amount) AS TotalAmount, COUNT(*) AS OrderCount, MAX(Amount) AS MaxAmount
// HAVING ((SUM(Amount) > 10000) AND (COUNT(*) >= 5))
```

---

## ⚙️ Configuration

### Schema Registry Integration
```csharp
// Configure Confluent Schema Registry client
var config = new SchemaRegistryConfig
{
    Url = "http://localhost:8081",
    BasicAuthUserInfo = "username:password"
};

var schemaClient = new ConfluentSchemaRegistryClient(config);

// Register schema for entity
var schema = SchemaGenerator.GenerateSchema<OrderEntity>();
await schemaClient.RegisterSchemaAsync("orders-value", schema);
```

### KSQL WITH Options
```csharp
var options = new KsqlWithOptions
{
    TopicName = "orders-topic",
    KeyFormat = "JSON",
    ValueFormat = "AVRO",
    Partitions = 3,
    Replicas = 2
};

var createStatement = KsqlCreateStatementBuilder.BuildCreateStatement(
    typeof(OrderEntity), 
    StreamTableType.Stream, 
    options
);
// Result: "CREATE STREAM OrderEntity (...) WITH (KAFKA_TOPIC='orders-topic', KEY_FORMAT='JSON', VALUE_FORMAT='AVRO', PARTITIONS=3, REPLICAS=2)"
```

---

## 📊 Examples

### Basic Query Building
```csharp
// Simple WHERE clause
Expression<Func<OrderEntity, bool>> whereExpr = o => o.Amount > 1000 && o.CustomerId == "CUST123";
var whereClause = new KsqlConditionBuilder().Build(whereExpr.Body);
// Result: "WHERE ((Amount > 1000) AND (CustomerId = 'CUST123'))"

// Projection with aliases
Expression<Func<OrderEntity, object>> selectExpr = o => new { 
    Id = o.OrderId, 
    Customer = o.CustomerId, 
    o.Amount 
};
var selectClause = new KsqlProjectionBuilder().Build(selectExpr.Body);
// Result: "SELECT OrderId AS Id, CustomerId AS Customer, Amount"
```

### Complex Scenarios
```csharp
// Complete query with multiple clauses
public class ComplexQueryExample
{
    public string BuildCompleteQuery()
    {
        // SELECT with aggregation
        var selectClause = KsqlAggregateBuilder.Build(
            (Expression<Func<IGrouping<string, OrderEntity>, object>>)
            (g => new { 
                CustomerId = g.Key,
                TotalAmount = g.Sum(x => x.Amount),
                OrderCount = g.Count()
            })
        );

        // FROM with JOIN
        var fromClause = "FROM OrderEntity o JOIN CustomerEntity c ON o.CustomerId = c.CustomerId";

        // WHERE conditions
        var whereClause = new KsqlConditionBuilder().Build(
            (Expression<Func<OrderEntity, bool>>)(o => o.Amount > 100)
        );

        // GROUP BY
        var groupByClause = KsqlGroupByBuilder.Build(
            (Expression<Func<OrderEntity, object>>)(o => o.CustomerId)
        );

        // HAVING clause
        var havingClause = new KsqlHavingBuilder().Build(
            (Expression<Func<IGrouping<string, OrderEntity>, bool>>)
            (g => g.Sum(x => x.Amount) > 1000)
        );

        // WINDOW clause
        var windowClause = new KsqlWindowBuilder().Build(
            (Expression<Func<ITumblingWindow>>)
            (() => Window.TumblingWindow().Size(TimeSpan.FromMinutes(5)))
        );

        return $"{selectClause} {fromClause} {windowClause} {whereClause} {groupByClause} {havingClause}";
    }
}
```

### Performance Patterns
```csharp
// Efficient property filtering
var schemaProperties = KsqlCreateStatementBuilder.GetSchemaProperties(typeof(OrderEntity));
var ignoredProperties = KsqlCreateStatementBuilder.GetIgnoredProperties(typeof(OrderEntity));

// Reusable builder instances
private static readonly KsqlConditionBuilder _conditionBuilder = new();
private static readonly KsqlProjectionBuilder _projectionBuilder = new();

// Batch schema generation
var entities = new[] { typeof(OrderEntity), typeof(CustomerEntity), typeof(ProductEntity) };
var createStatements = entities.Select(type => 
    KsqlCreateStatementBuilder.BuildCreateStatement(type, StreamTableType.Stream)
).ToArray();
```

---

## 📁 Project Structure

```
/src
  └── KsqlDsl                      → 実装コード
      ├── Ksql/                    → Core builders
      ├── Metadata/                → Type analysis
      └── Modeling/                → Attributes & configuration
/tests
  └── KsqlDslTests                 → Unit tests for LINQ → KSQL conversion
/claude_inputs                     → Design specs and prompts for Claude
  ├── specs/                       → 振る舞い/全体設計
  ├── tasks/                       → タスクごとの指示
  └── insights/                    → 考察・分析
/claude_outputs                    → Claude-generated code (for manual review)
/board_meeting                     → 議事録・意思決定記録
```

---

## ⚙️ Core Design Principles

- **Expression Tree Driven**  
  DSL relies entirely on `Expression<Func<...>>` inputs for transformation.

- **Composable Builders**  
  Each clause (e.g., `JOIN`, `WHERE`, `SELECT`, `HAVING`) is handled by a dedicated builder class.

- **Schema Customization**  
  Fine-grained control over Kafka schema generation with attributes like `[KafkaIgnore]`.

- **Testable by Design**  
  Tests follow the pattern:  
  `Expression → KSQL string`  
  Example:  
  ```csharp
  Expression<Func<IGrouping<string, Order>, object>> expr = g => new { Total = g.Sum(x => x.Amount) };
  // => SELECT SUM(Amount) AS Total
  ```

---

## 🤖 Claude Usage Guidelines

Claude is expected to:

1. Read design intent from this README and `claude_inputs/*.md`
2. Generate or improve builder code inside `/src`
3. Follow naming and formatting consistent with existing builder classes
4. Output new code in `/claude_outputs`, without modifying project files directly

Claude does **not**:
- Push changes to GitHub
- Run or validate tests
- Execute code (design-time only)

---

## ✅ Example Claude Prompt (used in /claude_inputs)

```
Implement a `KsqlHavingBuilder` that transforms:
g => g.Sum(x => x.Amount) > 1000
into:
HAVING SUM(Amount) > 1000

Make it expression-tree based and follow the same builder pattern as `KsqlWhereBuilder`.
```

---

## 🔁 Human-AI Collaboration Flow

1. Design → Documented by ChatGPT ("天城") into `claude_inputs/`
2. Implementation → Proposed by Claude ("鳴瀬")
3. Review & Integration → Performed in VSCode with GitHub + Copilot
4. Feedback → Iterated via ChatGPT and Claude

---

## 📚 Documentation

- **API Reference**: Auto-generated from XML documentation
- **Schema Registry Setup**: `docs/schema-registry-setup.md`
- **DLQ Monitoring**: `docs/dlq-monitoring-design.md`
- **Troubleshooting**: `docs/troubleshooting.md`
- **Performance Guide**: `docs/performance.md`

---

## 📌 Notes

- DSL is intended for compile-time query generation only.
- No runtime interpretation or reflection should be used.
- Precision-sensitive types (e.g., `decimal`) and time zones (e.g., `DateTimeOffset`) must retain schema fidelity.
- Properties marked with `[KafkaIgnore]` are excluded from all schema generation and KSQL statements.

---

## 🚀 Recent Updates

### v1.1.0 - Schema Customization
- ✅ **KafkaIgnore Attribute**: Exclude properties from schema generation
- ✅ **Enhanced CREATE Statement Builder**: Supports property filtering
- ✅ **Improved Documentation**: Comprehensive examples and usage patterns
- ✅ **Performance Optimizations**: Reflection caching for large-scale usage

### Coming Soon
- 🔄 **Schema Registry Integration**: Confluent.Kafka client support
- 🔄 **DLQ Monitoring**: Dead Letter Queue monitoring and alerting
- 🔄 **Advanced Type Mapping**: Custom type converters and schema evolution

---