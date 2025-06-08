using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Metadata;

/// <summary>
/// Represents the type of KSQL entity (STREAM or TABLE)
/// </summary>
internal enum StreamTableType
{
    Stream,
    Table
}
