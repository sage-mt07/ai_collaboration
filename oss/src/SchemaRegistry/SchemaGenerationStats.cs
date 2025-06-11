using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Statistics about schema generation
/// </summary>
public class SchemaGenerationStats
{
    public int TotalProperties { get; set; }
    public int IncludedProperties { get; set; }
    public int IgnoredProperties { get; set; }
    public List<string> IgnoredPropertyNames { get; set; } = new();
}