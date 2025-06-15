using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.KsqlDb;

internal class KsqlDbRestApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _ksqlDbUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed = false;

    public KsqlDbRestApiClient(string ksqlDbUrl, HttpClient? httpClient = null)
    {
        if (string.IsNullOrEmpty(ksqlDbUrl))
            throw new ArgumentException("ksqlDB URL cannot be null or empty", nameof(ksqlDbUrl));

        _ksqlDbUrl = ksqlDbUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();

        // 修正理由：task_eventset.txt「実データ送受信」に準拠、JSON設定
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// KSQL クエリを実行してPull Query結果を取得
    /// </summary>
    public async Task<KsqlQueryResponse> ExecuteQueryAsync(string ksqlQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ksqlQuery))
            throw new ArgumentException("KSQL query cannot be null or empty", nameof(ksqlQuery));

        // 修正理由：task_eventset.txt「Kafkaとの実データ送受信」に準拠
        var requestBody = new KsqlQueryRequest
        {
            Ksql = ksqlQuery,
            StreamsProperties = new Dictionary<string, object>
            {
                ["ksql.streams.auto.offset.reset"] = "earliest"
            }
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.ksql.v1+json");

        try
        {
            var response = await _httpClient.PostAsync($"{_ksqlDbUrl}/query", httpContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new KsqlDbException($"ksqlDB query failed: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseQueryResponse(responseContent);
        }
        catch (HttpRequestException ex)
        {
            throw new KsqlDbException($"Failed to connect to ksqlDB: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new KsqlDbException($"Query timeout: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// KSQL ステートメントを実行（CREATE, INSERT等）
    /// </summary>
    public async Task<KsqlStatementResponse> ExecuteStatementAsync(string ksqlStatement, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ksqlStatement))
            throw new ArgumentException("KSQL statement cannot be null or empty", nameof(ksqlStatement));

        var requestBody = new KsqlStatementRequest
        {
            Ksql = ksqlStatement
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, _jsonOptions);
        var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.ksql.v1+json");

        try
        {
            var response = await _httpClient.PostAsync($"{_ksqlDbUrl}/ksql", httpContent, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new KsqlDbException($"ksqlDB statement failed: {response.StatusCode} - {errorContent}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            return ParseStatementResponse(responseContent);
        }
        catch (HttpRequestException ex)
        {
            throw new KsqlDbException($"Failed to connect to ksqlDB: {ex.Message}", ex);
        }
    }

    private KsqlQueryResponse ParseQueryResponse(string responseContent)
    {
        try
        {
            // 修正理由：ksqlDB REST APIはJSONLines形式で応答するため行別解析
            var lines = responseContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var rows = new List<Dictionary<string, object>>();
            string[]? header = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var jsonDoc = JsonDocument.Parse(line);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("header", out var headerElement))
                {
                    // ヘッダー行の処理
                    header = ParseHeader(headerElement);
                }
                else if (root.TryGetProperty("row", out var rowElement) && header != null)
                {
                    // データ行の処理
                    var row = ParseDataRow(rowElement, header);
                    rows.Add(row);
                }
            }

            return new KsqlQueryResponse
            {
                Header = header ?? Array.Empty<string>(),
                Rows = rows
            };
        }
        catch (JsonException ex)
        {
            throw new KsqlDbException($"Failed to parse ksqlDB response: {ex.Message}", ex);
        }
    }

    private string[] ParseHeader(JsonElement headerElement)
    {
        if (headerElement.TryGetProperty("schema", out var schemaElement))
        {
            var headers = new List<string>();
            foreach (var column in schemaElement.EnumerateArray())
            {
                if (column.TryGetProperty("name", out var nameElement))
                {
                    headers.Add(nameElement.GetString() ?? "");
                }
            }
            return headers.ToArray();
        }
        return Array.Empty<string>();
    }

    private Dictionary<string, object> ParseDataRow(JsonElement rowElement, string[] header)
    {
        var row = new Dictionary<string, object>();

        if (rowElement.TryGetProperty("columns", out var columnsElement))
        {
            var values = new List<object>();
            foreach (var column in columnsElement.EnumerateArray())
            {
                values.Add(ExtractValue(column));
            }

            // ヘッダーと値をマッピング
            for (int i = 0; i < Math.Min(header.Length, values.Count); i++)
            {
                row[header[i]] = values[i];
            }
        }

        return row;
    }

    private object ExtractValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var longVal) ? longVal : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }

    private KsqlStatementResponse ParseStatementResponse(string responseContent)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(responseContent);
            return new KsqlStatementResponse
            {
                StatementText = jsonDoc.RootElement.GetProperty("statementText").GetString() ?? "",
                CommandId = jsonDoc.RootElement.TryGetProperty("commandId", out var cmdId) ? cmdId.GetString() : null
            };
        }
        catch (JsonException ex)
        {
            throw new KsqlDbException($"Failed to parse ksqlDB statement response: {ex.Message}", ex);
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

// 修正理由：task_eventset.txt「実データ送受信」に準拠したリクエスト/レスポンス型定義
public class KsqlQueryRequest
{
    public string Ksql { get; set; } = string.Empty;
    public Dictionary<string, object> StreamsProperties { get; set; } = new();
}

public class KsqlStatementRequest
{
    public string Ksql { get; set; } = string.Empty;
}

public class KsqlQueryResponse
{
    public string[] Header { get; set; } = Array.Empty<string>();
    public List<Dictionary<string, object>> Rows { get; set; } = new();
}

public class KsqlStatementResponse
{
    public string StatementText { get; set; } = string.Empty;
    public string? CommandId { get; set; }
}

public class KsqlDbException : Exception
{
    public KsqlDbException(string message) : base(message) { }
    public KsqlDbException(string message, Exception innerException) : base(message, innerException) { }
}