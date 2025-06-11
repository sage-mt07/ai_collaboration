using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Options for schema generation
/// </summary>
public class SchemaGenerationOptions
{
    /// <summary>
    /// Custom name for the schema (overrides type name)
    /// </summary>
    public string? CustomName { get; set; }

    /// <summary>
    /// Custom namespace for the schema
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Documentation for the schema
    /// </summary>
    public string? Documentation { get; set; }

    /// <summary>
    /// Whether to format JSON with indentation
    /// </summary>
    public bool PrettyFormat { get; set; } = true;

    /// <summary>
    /// Whether to use kebab-case for field names instead of camelCase
    /// </summary>
    public bool UseKebabCase { get; set; } = false;
}