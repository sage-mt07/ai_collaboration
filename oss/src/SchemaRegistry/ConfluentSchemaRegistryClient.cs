using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
// 修正理由：ConfluentクライアントとNullability対応using文追加
using Confluent.SchemaRegistry;

namespace KsqlDsl.SchemaRegistry.Implementation;

/// <summary>
/// Confluent Schema Registry client implementation for KsqlDsl
/// 修正理由：task_eventset.txt「Avroスキーマ連携を実際に実装」に準拠
/// </summary>
internal class ConfluentSchemaRegistryClient : ISchemaRegistryClient
{
    private readonly HttpClient _httpClient;
    private readonly string _schemaRegistryUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, AvroSchemaInfo> _schemaCache = new();
    // 修正理由：task_eventset.txt「Avroシリアライザー連携」のためConfluentクライアント保持
    private readonly Lazy<Confluent.SchemaRegistry.ISchemaRegistryClient> _confluentClient;
    private bool _disposed = false;

    public ConfluentSchemaRegistryClient(SchemaRegistryConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        _schemaRegistryUrl = config.Url.TrimEnd('/');
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMilliseconds(config.TimeoutMs)
        };

        // Basic認証設定
        if (!string.IsNullOrEmpty(config.BasicAuthUserInfo))
        {
            var authBytes = Encoding.ASCII.GetBytes(config.BasicAuthUserInfo);
            var authValue = Convert.ToBase64String(authBytes);
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", authValue);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // 修正理由：task_eventset.txt「Avroシリアライザー連携」のためConfluentクライアント初期化
        _confluentClient = new Lazy<Confluent.SchemaRegistry.ISchemaRegistryClient>(() =>
        {
            var confluentConfig = new Confluent.SchemaRegistry.SchemaRegistryConfig
            {
                Url = config.Url,
                BasicAuthUserInfo = config.BasicAuthUserInfo,
                RequestTimeoutMs = config.TimeoutMs,
                MaxCachedSchemas = config.MaxCachedSchemas
            };

            return new Confluent.SchemaRegistry.CachedSchemaRegistryClient(confluentConfig);
        });
    }

    /// <summary>
    /// トピック用のキー・値スキーマ同時登録
    /// 修正理由：task_eventset.txt「POCO→Avroスキーマを必ず突合」
    /// </summary>
    public async Task<(int keySchemaId, int valueSchemaId)> RegisterTopicSchemasAsync(
        string topicName, string keySchema, string valueSchema)
    {
        if (string.IsNullOrEmpty(topicName))
            throw new ArgumentException("Topic name cannot be null or empty", nameof(topicName));

        var keySubject = $"{topicName}-key";
        var valueSubject = $"{topicName}-value";

        var keySchemaId = await RegisterSchemaAsync(keySubject, keySchema);
        var valueSchemaId = await RegisterSchemaAsync(valueSubject, valueSchema);

        return (keySchemaId, valueSchemaId);
    }

    public async Task<int> RegisterKeySchemaAsync(string topicName, string keySchema)
    {
        var keySubject = $"{topicName}-key";
        return await RegisterSchemaAsync(keySubject, keySchema);
    }

    public async Task<int> RegisterValueSchemaAsync(string topicName, string valueSchema)
    {
        var valueSubject = $"{topicName}-value";
        return await RegisterSchemaAsync(valueSubject, valueSchema);
    }

    /// <summary>
    /// Avroスキーマ登録（メイン実装）
    /// 修正理由：task_eventset.txt「実データ送受信」に準拠
    /// </summary>
    public async Task<int> RegisterSchemaAsync(string subject, string avroSchema)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));
        if (string.IsNullOrEmpty(avroSchema))
            throw new ArgumentException("Avro schema cannot be null or empty", nameof(avroSchema));

        // キャッシュ確認
        var cacheKey = $"{subject}:{avroSchema.GetHashCode()}";
        if (_schemaCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.Id;
        }

        try
        {
            var requestBody = new
            {
                schema = avroSchema,
                schemaType = "AVRO"
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.schemaregistry.v1+json");

            var response = await _httpClient.PostAsync($"{_schemaRegistryUrl}/subjects/{subject}/versions", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to register schema for subject '{subject}': {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);
            var schemaId = responseDoc.RootElement.GetProperty("id").GetInt32();

            // キャッシュに保存
            _schemaCache[cacheKey] = new AvroSchemaInfo
            {
                Id = schemaId,
                Subject = subject,
                AvroSchema = avroSchema
            };

            return schemaId;
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Schema Registry: {ex.Message}", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Failed to parse Schema Registry response: {ex.Message}", ex);
        }
    }

    public async Task<AvroSchemaInfo> GetLatestSchemaAsync(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        try
        {
            var response = await _httpClient.GetAsync($"{_schemaRegistryUrl}/subjects/{subject}/versions/latest");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to get latest schema for subject '{subject}': {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseSchemaInfo(responseContent);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Schema Registry: {ex.Message}", ex);
        }
    }

    public async Task<AvroSchemaInfo> GetSchemaByIdAsync(int schemaId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_schemaRegistryUrl}/schemas/ids/{schemaId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to get schema by ID {schemaId}: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseSchemaInfo(responseContent);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Schema Registry: {ex.Message}", ex);
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
            var jsonContent = JsonSerializer.Serialize(requestBody, _jsonOptions);
            var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.schemaregistry.v1+json");

            var response = await _httpClient.PostAsync($"{_schemaRegistryUrl}/compatibility/subjects/{subject}/versions/latest", httpContent);

            if (!response.IsSuccessStatusCode)
            {
                return false; // 互換性なし
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);
            return responseDoc.RootElement.TryGetProperty("is_compatible", out var compat) && compat.GetBoolean();
        }
        catch
        {
            return false; // エラー時は互換性なしとして扱う
        }
    }

    public async Task<IList<int>> GetSchemaVersionsAsync(string subject)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        try
        {
            var response = await _httpClient.GetAsync($"{_schemaRegistryUrl}/subjects/{subject}/versions");

            if (!response.IsSuccessStatusCode)
            {
                return new List<int>(); // バージョンなし
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);
            var versions = new List<int>();

            foreach (var element in responseDoc.RootElement.EnumerateArray())
            {
                versions.Add(element.GetInt32());
            }

            return versions;
        }
        catch
        {
            return new List<int>(); // エラー時は空リスト
        }
    }

    public async Task<AvroSchemaInfo> GetSchemaAsync(string subject, int version)
    {
        if (string.IsNullOrEmpty(subject))
            throw new ArgumentException("Subject cannot be null or empty", nameof(subject));

        try
        {
            var response = await _httpClient.GetAsync($"{_schemaRegistryUrl}/subjects/{subject}/versions/{version}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Failed to get schema for subject '{subject}' version {version}: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseSchemaInfo(responseContent);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"Failed to connect to Schema Registry: {ex.Message}", ex);
        }
    }

    public async Task<IList<string>> GetAllSubjectsAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_schemaRegistryUrl}/subjects");

            if (!response.IsSuccessStatusCode)
            {
                return new List<string>(); // サブジェクトなし
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var responseDoc = JsonDocument.Parse(responseContent);
            var subjects = new List<string>();

            foreach (var element in responseDoc.RootElement.EnumerateArray())
            {
                var subject = element.GetString();
                if (!string.IsNullOrEmpty(subject))
                {
                    subjects.Add(subject);
                }
            }

            return subjects;
        }
        catch
        {
            return new List<string>(); // エラー時は空リスト
        }
    }

    private AvroSchemaInfo ParseSchemaInfo(string responseContent)
    {
        var responseDoc = JsonDocument.Parse(responseContent);
        var root = responseDoc.RootElement;

        return new AvroSchemaInfo
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
            Version = root.TryGetProperty("version", out var version) ? version.GetInt32() : 0,
            Subject = root.TryGetProperty("subject", out var subject) ? subject.GetString() ?? "" : "",
            AvroSchema = root.TryGetProperty("schema", out var schema) ? schema.GetString() ?? "" : ""
        };
    }

    /// <summary>
    /// Confluent Avroシリアライザー用のクライアント取得
    /// 修正理由：task_eventset.txt「Avroシリアライザー連携」
    /// </summary>
    internal Confluent.SchemaRegistry.ISchemaRegistryClient GetConfluentClient()
    {
        return _confluentClient.Value;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient?.Dispose();
            // 修正理由：Confluentクライアントも適切にDispose
            if (_confluentClient.IsValueCreated)
            {
                _confluentClient.Value?.Dispose();
            }
            _schemaCache.Clear();
            _disposed = true;
        }
    }
}