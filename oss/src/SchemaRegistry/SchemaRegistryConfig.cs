using System.Collections.Generic;

namespace KsqlDsl.SchemaRegistry;


public class SchemaRegistryConfig
{
    public string Url { get; set; } = "http://localhost:8081";

    public string? BasicAuthUserInfo { get; set; }

    public int TimeoutMs { get; set; } = 30000;

    public int MaxCachedSchemas { get; set; } = 1000;

    public Dictionary<string, string> Properties { get; set; } = new();
}