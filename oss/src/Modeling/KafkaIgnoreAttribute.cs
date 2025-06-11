using System;

namespace KsqlDsl.Modeling
{
    /// <summary>
    /// Marks a property to be excluded from Kafka schema generation and KSQL CREATE statements.
    /// When applied to a POCO property, the property will not be included in:
    /// - KSQL CREATE STREAM/TABLE column definitions
    /// - Schema registry schema generation
    /// - Kafka serialization schema
    /// </summary>
    /// <example>
    /// <code>
    /// public class OrderEntity
    /// {
    ///     public int OrderId { get; set; }
    ///     public string CustomerId { get; set; }
    ///     
    ///     [KafkaIgnore]
    ///     public DateTime InternalTimestamp { get; set; } // Excluded from schema
    ///     
    ///     [KafkaIgnore]
    ///     public string DebugInfo { get; set; } // Excluded from schema
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public class KafkaIgnoreAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the KafkaIgnoreAttribute class.
        /// </summary>
        public KafkaIgnoreAttribute()
        {
        }

        /// <summary>
        /// Optional reason for ignoring this property.
        /// Useful for documentation and debugging purposes.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Returns a string representation of this attribute.
        /// </summary>
        /// <returns>A string describing the ignore directive and optional reason.</returns>
        public override string ToString()
        {
            return string.IsNullOrEmpty(Reason) 
                ? "KafkaIgnore" 
                : $"KafkaIgnore: {Reason}";
        }
    }
}