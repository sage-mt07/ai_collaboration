using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry;

/// <summary>
/// Schema registry client interface for registering and managing Kafka schemas (Avro only)
/// </summary>
public interface ISchemaRegistryClient : IDisposable
{
    /// <summary>
    /// Registers both key and value schemas for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="keySchema">The key schema (Avro format)</param>
    /// <param name="valueSchema">The value schema (Avro format)</param>
    /// <returns>Tuple of (keySchemaId, valueSchemaId)</returns>
    Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema);

    /// <summary>
    /// Registers a key schema for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="keySchema">The key schema (Avro format)</param>
    /// <returns>The key schema ID</returns>
    Task<int> RegisterKeySchemaAsync(string topicName, string keySchema);

    /// <summary>
    /// Registers a value schema for the specified topic
    /// </summary>
    /// <param name="topicName">The topic name</param>
    /// <param name="valueSchema">The value schema (Avro format)</param>
    /// <returns>The value schema ID</returns>
    Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema);

    /// <summary>
    /// Registers a new schema for the specified subject (legacy method)
    /// </summary>
    /// <param name="subject">The subject name (typically topic-value or topic-key)</param>
    /// <param name="schema">The schema to register</param>
    /// <returns>The schema ID assigned by the registry</returns>
    Task<int> RegisterSchemaAsync(string subject, string schema);

    /// <summary>
    /// Gets the latest schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <returns>The latest schema information</returns>
    Task<SchemaInfo> GetLatestSchemaAsync(string subject);

    /// <summary>
    /// Gets a specific schema by ID
    /// </summary>
    /// <param name="schemaId">The schema ID</param>
    /// <returns>The schema information</returns>
    Task<SchemaInfo> GetSchemaByIdAsync(int schemaId);

    /// <summary>
    /// Checks if a schema is compatible with the latest version of the subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <param name="schema">The schema to check</param>
    /// <returns>True if compatible, false otherwise</returns>
    Task<bool> CheckCompatibilityAsync(string subject, string schema);

    /// <summary>
    /// Gets all versions of a schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <returns>List of version numbers</returns>
    Task<IList<int>> GetSchemaVersionsAsync(string subject);

    /// <summary>
    /// Gets a specific version of a schema for the specified subject
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <param name="version">The version number</param>
    /// <returns>The schema information</returns>
    Task<SchemaInfo> GetSchemaAsync(string subject, int version);

    /// <summary>
    /// Deletes a specific version of a schema
    /// </summary>
    /// <param name="subject">The subject name</param>
    /// <param name="version">The version to delete</param>
    /// <returns>The deleted version number</returns>
    Task<int> DeleteSchemaAsync(string subject, int version);

    /// <summary>
    /// Gets all subjects registered in the schema registry
    /// </summary>
    /// <returns>List of subject names</returns>
    Task<IList<string>> GetAllSubjectsAsync();
}

