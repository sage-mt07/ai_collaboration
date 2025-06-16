using KsqlDsl.Metadata;
using KsqlDsl.Modeling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace KsqlDsl.Ksql;

internal static class KsqlCreateStatementBuilder
{

    public static string BuildCreateStatement(Type entityType, StreamTableType type, KsqlWithOptions? options = null)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));

        if (!Enum.IsDefined(typeof(StreamTableType), type))
            throw new ArgumentException($"Invalid StreamTableType: {type}", nameof(type));

        var keyword = type == StreamTableType.Stream ? "STREAM" : "TABLE";
        var tableName = entityType.Name;
        var columns = BuildColumnDefinitions(entityType);
        var withClause = options?.BuildWithClause() ?? "";

        return $"CREATE {keyword} {tableName} ({columns}){withClause}";
    }

    public static string BuildCreateStatementWithInference(Type entityType, Expression linqExpression, KsqlWithOptions? options = null)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));
        if (linqExpression == null)
            throw new ArgumentNullException(nameof(linqExpression));

        var analyzer = new StreamTableInferenceAnalyzer();
        var inferenceResult = analyzer.AnalyzeExpression(linqExpression);

        return BuildCreateStatement(entityType, inferenceResult.InferredType, options);
    }
    public static InferenceResult InferStreamTableType(Expression linqExpression)
    {
        if (linqExpression == null)
            throw new ArgumentNullException(nameof(linqExpression));

        var analyzer = new StreamTableInferenceAnalyzer();
        return analyzer.AnalyzeExpression(linqExpression);
    }
    private static string BuildColumnDefinitions(Type entityType)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columnDefinitions = new List<string>();

        foreach (var property in properties)
        {
            // Skip properties marked with [KafkaIgnore]
            if (ShouldIgnoreProperty(property))
            {
                continue;
            }

            var columnName = property.Name;
            var ksqlType = GetKsqlType(property);
            columnDefinitions.Add($"{columnName} {ksqlType}");
        }

        return string.Join(", ", columnDefinitions);
    }

    private static bool ShouldIgnoreProperty(PropertyInfo property)
    {
        return property.GetCustomAttribute<KafkaIgnoreAttribute>() != null;
    }

    public static PropertyInfo[] GetSchemaProperties(Type entityType)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));

        return entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !ShouldIgnoreProperty(property))
            .ToArray();
    }

    public static PropertyInfo[] GetIgnoredProperties(Type entityType)
    {
        if (entityType == null)
            throw new ArgumentNullException(nameof(entityType));

        return entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(ShouldIgnoreProperty)
            .ToArray();
    }

    private static string GetKsqlType(PropertyInfo property)
    {
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        // Handle decimal with precision attribute
        if (underlyingType == typeof(decimal))
        {
            var decimalAttr = property.GetCustomAttribute<DecimalPrecisionAttribute>();
            if (decimalAttr != null)
            {
                return $"DECIMAL({decimalAttr.Precision}, {decimalAttr.Scale})";
            }
            return "DECIMAL";
        }

        // Map standard types
        return underlyingType switch
        {
            Type t when t == typeof(int) => "INT",
            Type t when t == typeof(long) => "BIGINT",
            Type t when t == typeof(short) => "INT", // KSQL doesn't have SMALLINT
            Type t when t == typeof(float) => "DOUBLE", // KSQL uses DOUBLE for floating point
            Type t when t == typeof(double) => "DOUBLE",
            Type t when t == typeof(bool) => "BOOLEAN",
            Type t when t == typeof(string) => "VARCHAR",
            Type t when t == typeof(DateTime) => "TIMESTAMP",
            Type t when t == typeof(DateTimeOffset) => "TIMESTAMP",
            Type t when t == typeof(Guid) => "VARCHAR", // GUIDs are typically stored as strings
            Type t when t == typeof(char) => "VARCHAR", // Single char as VARCHAR
            _ => "VARCHAR" // Default fallback
        };
    }
}