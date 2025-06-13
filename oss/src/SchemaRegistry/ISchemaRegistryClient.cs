using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Schema registry client interface for registering and managing Kafka Avro schemas ONLY
/// KsqlDsl supports Avro format exclusively for schema management
/// </summary>
public interface ISchemaRegistryClient : IDisposable
{
    /// <summary>
    /// Registers both key and value Avro schemas for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="keySchema">The key schema (Avro format)</param>
    /// <param name="valueSchema">The value schema (Avro format)</param>
    /// <returns>Tuple of (keySchemaId, valueSchemaId)</returns>
    Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema);

    /// <summary>
    /// Registers an Avro key schema for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="keySchema">The key schema (Avro format)</param>
    /// <returns>The key schema ID</returns>
    Task<int> RegisterKeySchemaAsync(string topicName, string keySchema);

    /// <summary>
    /// Registers an Avro value schema for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="valueSchema">The value schema (Avro format)</param>
    /// <returns>The value schema ID</returns>
    Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema);

    /// <summary>
    /// Registers a new Avro schema for the specified subject (legacy method)
    /// </summary>
    /// <param name="subject">The subject name (typically topic-value or topic-key)</param>
    /// <param name="avroSchema">The Avro schema to register</param>
    /// <returns>The schema ID assigned by the registry</returns>
    Task<int> RegisterSchemaAsync(string subject, string avroSchema);

    /// <summary>
    /// Gets the latest Avro schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <returns>The latest Avro schema information</returns>
    Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject);

    /// <summary>
    /// Gets a specific Avro schema by ID
    /// </summary>
    /// <param name="schemaId">The schema ID</param>
    /// <returns>The Avro schema information</returns>
    Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId);

    /// <summary>
    /// Checks if an Avro schema is compatible with the latest version of the subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <param name="avroSchema">The Avro schema to check</param>
    /// <returns>True if compatible, false otherwise</returns>
    Task<bool> CheckCompatibilityAsync(string subject, string avroSchema);

    /// <summary>
    /// Gets all versions of an Avro schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <returns>List of version numbers</returns>
    Task<IList<int>> GetSchemaVersionsAsync(string subject);

    /// <summary>
    /// Gets a specific version of an Avro schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <param name="version">The version number</param>
    /// <returns>The Avro schema information</returns>
    Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version);

    /// <summary>
    /// Gets all subjects registered in the schema registry
    /// </summary>
    /// <returns>List of subject names</returns>
    Task<IList<string>> GetAllSubjectsAsync();
}

/// <summary>
/// Avro schema information container (KsqlDsl specific)
/// </summary>
public class AvroSchemaInfo
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string AvroSchema { get; set; } = string.Empty;
    
    /// <summary>
    /// Always Avro in KsqlDsl context
    /// </summary>
    public string SchemaFormat => "AVRO";
}