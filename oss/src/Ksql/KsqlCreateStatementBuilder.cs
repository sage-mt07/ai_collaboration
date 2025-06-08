using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Ksql.EntityFrameworkCore.Modeling;
using KsqlDsl.Metadata;

namespace KsqlDsl.Ksql;


/// <summary>
/// Builder for generating KSQL CREATE STREAM/TABLE DDL statements from C# POCO types
/// </summary>
internal static class KsqlCreateStatementBuilder
{
    /// <summary>
    /// Builds a KSQL CREATE STREAM or CREATE TABLE statement from a C# type
    /// </summary>
    /// <param name="entityType">The C# type to generate DDL for</param>
    /// <param name="type">Whether to generate STREAM or TABLE</param>
    /// <param name="options">Optional WITH clause configuration</param>
    /// <returns>Complete KSQL CREATE statement</returns>
    /// <exception cref="ArgumentNullException">When entityType is null</exception>
    /// <exception cref="ArgumentException">When type is invalid</exception>
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

    /// <summary>
    /// Builds a KSQL CREATE STREAM or CREATE TABLE statement with automatic type inference from LINQ expression
    /// </summary>
    /// <param name="entityType">The C# type to generate DDL for</param>
    /// <param name="linqExpression">LINQ expression to analyze for STREAM/TABLE inference</param>
    /// <param name="options">Optional WITH clause configuration</param>
    /// <returns>Complete KSQL CREATE statement with inferred type</returns>
    /// <exception cref="ArgumentNullException">When entityType or linqExpression is null</exception>
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

    /// <summary>
    /// Analyzes a LINQ expression to infer whether it should be a STREAM or TABLE
    /// </summary>
    /// <param name="linqExpression">LINQ expression to analyze</param>
    /// <returns>Inference result with type and reasoning</returns>
    /// <exception cref="ArgumentNullException">When linqExpression is null</exception>
    public static InferenceResult InferStreamTableType(Expression linqExpression)
    {
        if (linqExpression == null)
            throw new ArgumentNullException(nameof(linqExpression));

        var analyzer = new StreamTableInferenceAnalyzer();
        return analyzer.AnalyzeExpression(linqExpression);
    }

    /// <summary>
    /// Builds column definitions from a C# type's properties
    /// </summary>
    /// <param name="entityType">The C# type to analyze</param>
    /// <returns>Comma-separated column definitions</returns>
    private static string BuildColumnDefinitions(Type entityType)
    {
        var properties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columnDefinitions = new List<string>();

        foreach (var property in properties)
        {
            var columnName = property.Name;
            var ksqlType = GetKsqlType(property);
            columnDefinitions.Add($"{columnName} {ksqlType}");
        }

        return string.Join(", ", columnDefinitions);
    }

    /// <summary>
    /// Maps C# property types to KSQL types, considering custom attributes
    /// </summary>
    /// <param name="property">The property to analyze</param>
    /// <returns>KSQL type string</returns>
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