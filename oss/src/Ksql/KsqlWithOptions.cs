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

        // Add standard options in a consistent order
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
            if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
            {
                options.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return options.Any() ? $" WITH ({string.Join(", ", options)})" : "";
    }

    /// <summary>
    /// Adds an additional option to the WITH clause
    /// </summary>
    /// <param name="key">Option key</param>
    /// <param name="value">Option value</param>
    /// <returns>This instance for method chaining</returns>
    public KsqlWithOptions AddOption(string key, string value)
    {
        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
        {
            AdditionalOptions[key] = value;
        }
        return this;
    }

    /// <summary>
    /// Removes an additional option from the WITH clause
    /// </summary>
    /// <param name="key">Option key to remove</param>
    /// <returns>This instance for method chaining</returns>
    public KsqlWithOptions RemoveOption(string key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            AdditionalOptions.Remove(key);
        }
        return this;
    }

    /// <summary>
    /// Clears all additional options
    /// </summary>
    /// <returns>This instance for method chaining</returns>
    public KsqlWithOptions ClearAdditionalOptions()
    {
        AdditionalOptions.Clear();
        return this;
    }
}