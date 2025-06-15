using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KsqlDsl.SchemaRegistry.Implementation;

/// <summary>
/// Confluent Schema Registry実装
/// 修正理由：Phase3-4でKafkaProducerService・KafkaConsumerServiceの依存関係解決
/// </summary>
internal class ConfluentSchemaRegistryClient : ISchemaRegistryClient
{
    private readonly HttpClient _httpClient;
    private readonly SchemaRegistryConfig _config;
    private readonly Dictionary<string, AvroSchemaInfo> _schemaCache = new();
    private bool _disposed = false;

    public ConfluentSchemaRegistryClient(SchemaRegistryConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = new HttpClient();

        // 修正理由：Basic認証設定
        if (!string.IsNullOrEmpty(_config.BasicAuthUserInfo))
        {
            var authBytes = Encoding.UTF8.GetBytes(_config.BasicAuthUserInfo);
            var authHeader = Convert.ToBase64String(authBytes);
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authHeader);
        }

        _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
    }

    public async Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(string topicName, string keySchema, string valueSchema)
    {
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
        if (string.IsNullOrEmpty(keySchema))
            throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));
        if (string.IsNullOrEmpty(valueSchema))
            throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

        var keySubject = $"{topicName}-key";
        var valueSubject = $"{topicName}-value";

        var keyTask = RegisterSchemaAsync(keySubject, keySchema);
        var valueTask = RegisterSchemaAsync(valueSubject, valueSchema);

        await Task.WhenAll(keyTask, valueTask);

        return (await keyTask, await valueTask);
    }

    public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
    {
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
        if (string.IsNullOrEmpty(keySchema))
            throw new ArgumentException("Key schema cannot be null or empty", nameof(keySchema));

        var keySubject = $"{topicName}-key";
        return await RegisterSchemaAsync(keySubject, keySchema);
    }

    public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
    {
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));
        if (string.IsNullOrEmpty(valueSchema))
            throw new ArgumentException("Value schema cannot be null or empty", nameof(valueSchema));

        var valueSubject = $"{topicName}-value";
        return await RegisterSchemaAsync(valueSubject, valueSchema);
    }

    public async Task<int> RegisterSchemaAsync(string subject, string avroSchema)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
        if (string.IsNullOrEmpty(avroSchema))
            throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

        try
        {
            var requestBody = new { schema = avroSchema };
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.schemaregistry.v1+json");

            var response = await _httpClient.PostAsync($"{_config.Url}/subjects/{subject}/versions", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to register schema for subject '{subject}': {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);

            if (responseDoc.RootElement.TryGetProperty("id", out var idElement))
            {
                return idElement.GetInt32();
            }

            throw new SchemaRegistryException($"Invalid response from schema registry: missing 'id' field");
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new SchemaRegistryException($"Failed to parse schema registry response: {ex.Message}", ex);
        }
    }

    public async Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        // 修正理由：Phase3-4でキャッシュ機能追加
        if (_schemaCache.TryGetValue($"{subject}:latest", out var cachedSchema))
        {
            return cachedSchema;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_config.Url}/subjects/{subject}/versions/latest");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to get latest schema for subject '{subject}': {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var schemaInfo = ParseSchemaResponse(responseContent, subject);

            // キャッシュに保存
            _schemaCache[$"{subject}:latest"] = schemaInfo;
            _schemaCache[$"{subject}:{schemaInfo.Version}"] = schemaInfo;

            return schemaInfo;
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
    }

    public async Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.Url}/schemas/ids/{schemaId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to get schema by ID {schemaId}: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseSchemaResponse(responseContent, $"id-{schemaId}");
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
    }

    public async Task<bool> CheckCompatibilityAsync(string subject, string avroSchema)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
        if (string.IsNullOrEmpty(avroSchema))
            throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

        try
        {
            var requestBody = new { schema = avroSchema };
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.schemaregistry.v1+json");

            var response = await _httpClient.PostAsync($"{_config.Url}/compatibility/subjects/{subject}/versions/latest", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                return false; // 互換性チェック失敗は非互換とみなす
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);

            if (responseDoc.RootElement.TryGetProperty("is_compatible", out var compatibleElement))
            {
                return compatibleElement.GetBoolean();
            }

            return false;
        }
        catch (Exception)
        {
            return false; // エラー時は非互換とみなす
        }
    }

    public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        try
        {
            var response = await _httpClient.GetAsync($"{_config.Url}/subjects/{subject}/versions");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to get schema versions for subject '{subject}': {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var versions = JsonSerializer.Deserialize<int[]>(responseContent) ?? Array.Empty<int>();

            return versions;
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new SchemaRegistryException($"Failed to parse schema registry response: {ex.Message}", ex);
        }
    }

    public async Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        // キャッシュチェック
        if (_schemaCache.TryGetValue($"{subject}:{version}", out var cachedSchema))
        {
            return cachedSchema;
        }

        try
        {
            var response = await _httpClient.GetAsync($"{_config.Url}/subjects/{subject}/versions/{version}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to get schema for subject '{subject}' version {version}: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var schemaInfo = ParseSchemaResponse(responseContent, subject);

            // キャッシュに保存
            _schemaCache[$"{subject}:{version}"] = schemaInfo;

            return schemaInfo;
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
    }

    public async Task<IList<string>> GetAllSubjectsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_config.Url}/subjects");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new SchemaRegistryException($"Failed to get all subjects: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var subjects = JsonSerializer.Deserialize<string[]>(responseContent) ?? Array.Empty<string>();

            return subjects;
        }
        catch (HttpRequestException ex)
        {
            throw new SchemaRegistryException($"Failed to connect to schema registry: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new SchemaRegistryException($"Failed to parse schema registry response: {ex.Message}", ex);
        }
    }

    private AvroSchemaInfo ParseSchemaResponse(string responseContent, string subject)
    {
        try
        {
            var responseDoc = JsonDocument.Parse(responseContent);
            var root = responseDoc.RootElement;

            var id = root.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : 0;
            var version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetInt32() : 0;
            var schema = root.TryGetProperty("schema", out var schemaElement) ? schemaElement.GetString() ?? "" : "";

            return new AvroSchemaInfo
            {
                Id = id,
                Version = version,
                Subject = subject,
                AvroSchema = schema
            };
        }
        catch (JsonException ex)
        {
            throw new SchemaRegistryException($"Failed to parse schema response: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Schema Registry例外
/// 修正理由：Phase3-4でSchema Registry専用例外クラス追加
/// </summary>
public class SchemaRegistryException : Exception
{
    public SchemaRegistryException(string message) : base(message) { }
    public SchemaRegistryException(string message, Exception innerException) : base(message, innerException) { }
}