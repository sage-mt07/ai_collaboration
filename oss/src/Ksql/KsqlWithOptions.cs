using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Ksql;

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
