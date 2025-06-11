using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Avro field representation
/// </summary>
public class AvroField
{
    public string Name { get; set; } = string.Empty;
    public object Type { get; set; } = string.Empty;
    public string? Doc { get; set; }
    public object? Default { get; set; }
}
