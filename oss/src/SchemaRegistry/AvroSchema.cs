using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;


/// <summary>
/// Avro schema representation
/// </summary>
public class AvroSchema
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public string? Doc { get; set; }
    public List<AvroField> Fields { get; set; } = new();
}