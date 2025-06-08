using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace KsqlDsl
{
    /// <summary>
    /// Represents the type of KSQL entity (STREAM or TABLE)
    /// </summary>
    public enum StreamTableType
    {
        Stream,
        Table
    }

    /// <summary>
    /// Configuration options for KSQL CREATE statements WITH clause
    /// </summary>
    public class KsqlWithOptions
    {
        public string? TopicName { get; set; }
        public string? KeyFormat { get; set; }
        public string? ValueFormat { get; set; }
        public int? Partitions { get; set; }
        public int? Replicas { get; set; }
        public Dictionary<string, string> AdditionalOptions { get; set; } = new();

        /// <summary>
        /// Builds the WITH clause string from the configured options
        /// </summary>
        /// <returns>WITH clause string or empty string if no options are set</returns>
        public string BuildWithClause()
        {
            var options = new List<string>();

            if (!string.IsNullOrEmpty(TopicName))
                options.Add($"KAFKA_TOPIC='{TopicName}'");

            if (!string.IsNullOrEmpty(KeyFormat))
                options.Add($"KEY_FORMAT='{KeyFormat}'");

            if (!string.IsNullOrEmpty(ValueFormat))
                options.Add($"VALUE_FORMAT='{ValueFormat}'");

            if (Partitions.HasValue)
                options.Add($"PARTITIONS={Partitions.Value}");

            if (Replicas.HasValue)
                options.Add($"REPLICAS={Replicas.Value}");

            // Add any additional options
            foreach (var kvp in AdditionalOptions)
            {
                options.Add($"{kvp.Key}={kvp.Value}");
            }

            return options.Any() ? $" WITH ({string.Join(", ", options)})" : "";
        }
    }

    /// <summary>
    /// Result of LINQ expression analysis for Stream/Table inference
    /// </summary>
    public class InferenceResult
    {
        public StreamTableType InferredType { get; set; }
        public bool IsExplicitlyDefined { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Analyzer for inferring STREAM vs TABLE based on LINQ expression patterns
    /// </summary>
    public class StreamTableInferenceAnalyzer : ExpressionVisitor
    {
        private bool _hasGroupBy = false;
        private bool _hasAggregate = false;
        private bool _hasWindow = false;
        private bool _hasAsStream = false;
        private bool _hasAsTable = false;
        private readonly HashSet<string> _aggregateMethods = new() 
        {
            "Sum", "Count", "Max", "Min", "Average", "Avg",
            "LatestByOffset", "EarliestByOffset", "CollectList", "CollectSet"
        };

        public InferenceResult AnalyzeExpression(Expression expression)
        {
            // Reset state
            _hasGroupBy = _hasAggregate = _hasWindow = _hasAsStream = _hasAsTable = false;
            
            Visit(expression);
            
            return DetermineResult();
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            var methodName = node.Method.Name;

            switch (methodName)
            {
                case "GroupBy":
                    _hasGroupBy = true;
                    break;
                case "AsStream":
                    _hasAsStream = true;
                    break;
                case "AsTable":
                    _hasAsTable = true;
                    break;
                case "Window":
                case "TumblingWindow":
                case "HoppingWindow":
                case "SessionWindow":
                    _hasWindow = true;
                    break;
                default:
                    // Check if method name contains aggregate function names or is a mock method
                    if (_aggregateMethods.Contains(methodName) || 
                        methodName.Contains("Count") || 
                        methodName.StartsWith("Mock"))
                        _hasAggregate = true;
                    break;
            }

            return base.VisitMethodCall(node);
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            // Visit the lambda body to analyze the expression
            Visit(node.Body);
            return node;
        }

        private InferenceResult DetermineResult()
        {
            // Explicit markers take highest priority
            if (_hasAsStream)
                return new InferenceResult 
                { 
                    InferredType = StreamTableType.Stream, 
                    IsExplicitlyDefined = true,
                    Reason = "Explicit .AsStream() marker"
                };

            if (_hasAsTable)
                return new InferenceResult 
                { 
                    InferredType = StreamTableType.Table, 
                    IsExplicitlyDefined = true,
                    Reason = "Explicit .AsTable() marker"
                };

            // Pattern-based inference
            if (_hasGroupBy || _hasAggregate || _hasWindow)
                return new InferenceResult 
                { 
                    InferredType = StreamTableType.Table, 
                    IsExplicitlyDefined = false,
                    Reason = $"Contains {(_hasGroupBy ? "GroupBy" : _hasAggregate ? "Aggregate" : "Window")} operation"
                };

            // Default to STREAM for simple operations like Where/Select
            return new InferenceResult 
            { 
                InferredType = StreamTableType.Stream, 
                IsExplicitlyDefined = false,
                Reason = "Simple query without aggregation (default to STREAM)"
            };
        }
    }

    /// <summary>
    /// Builder for generating KSQL CREATE STREAM/TABLE DDL statements from C# POCO types
    /// </summary>
    public static class KsqlCreateStatementBuilder
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
}