using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;


/// <summary>
/// Configuration for schema registry client
/// </summary>
public class SchemaRegistryConfig
{
    /// <summary>
    /// Schema registry URL
    /// </summary>
    public string Url { get; set; } = "http://localhost:8081";

    /// <summary>
    /// Basic authentication credentials in format "username:password"
    /// </summary>
    public string? BasicAuthUserInfo { get; set; }

    /// <summary>
    /// Request timeout in milliseconds
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Maximum number of cached schemas
    /// </summary>
    public int MaxCachedSchemas { get; set; } = 1000;

    /// <summary>
    /// Additional client properties
    /// </summary>
    public Dictionary<string, string> Properties { get; set; } = new();
}