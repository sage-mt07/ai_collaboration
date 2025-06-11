using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Schema information container
/// </summary>
public class SchemaInfo
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public SchemaType SchemaType { get; set; } = SchemaType.Avro;
}
