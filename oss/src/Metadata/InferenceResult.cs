using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Metadata;


/// <summary>
/// Result of LINQ expression analysis for Stream/Table inference
/// </summary>
public class InferenceResult
{
    public StreamTableType InferredType { get; set; }
    public bool IsExplicitlyDefined { get; set; }
    public string Reason { get; set; } = "";
}