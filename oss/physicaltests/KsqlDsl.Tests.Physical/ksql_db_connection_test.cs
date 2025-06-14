using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using Xunit;

public class KsqlDb_ConnectionTest
{
    private const string BootstrapServers = "localhost:9093"; // 外部接続用ポート
    private const string KsqlDbUrl = "http://localhost:8088";
    private const string TopicName = "test_topic";
    private const string StreamName = "test_stream";

    [Fact]
    public async Task Should_Produce_And_Query_KsqlDb()
    {
        // ① Kafka に JSON データを produce（トピック自動作成に依存）
        var config = new ProducerConfig 
        { 
            BootstrapServers = BootstrapServers,
            // 追加: 接続タイムアウト設定
            MessageTimeoutMs = 10000,
            RequestTimeoutMs = 5000
        };
        
        using var producer = new ProducerBuilder<Null, string>(config).Build();

        var messageJson = "{\"id\": 1, \"name\": \"test\"}";
        
        try 
        {
            var deliveryResult = await producer.ProduceAsync(TopicName, new Message<Null, string> { Value = messageJson });
            Console.WriteLine($"Message delivered to {deliveryResult.TopicPartitionOffset}");
        }
        catch (ProduceException<Null, string> e)
        {
            Console.WriteLine($"Delivery failed: {e.Error.Reason}");
            throw;
        }
        
        producer.Flush(TimeSpan.FromSeconds(10)); // タイムアウトを延長

        // 待機を延長（ksqlDBでの処理時間を考慮）
        await Task.Delay(5000);

        // ② ksqlDB に Stream を作成
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30); // タイムアウト設定
        
        var createStreamSql = $"""
            CREATE STREAM IF NOT EXISTS {StreamName} (
              id INT,
              name STRING
            ) WITH (
              KAFKA_TOPIC='{TopicName}',
              VALUE_FORMAT='JSON'
            );
        """;

        var payload = JsonSerializer.Serialize(new { ksql = createStreamSql });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        
        try 
        {
            var response = await client.PostAsync($"{KsqlDbUrl}/ksql", content);
            var createResult = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Create stream response: {createResult}");
            response.EnsureSuccessStatusCode();
        }
        catch (HttpRequestException e)
        {
            Console.WriteLine($"ksqlDB connection failed: {e.Message}");
            throw;
        }

        // ③ Stream を SELECT して取得確認
        var querySql = $"SELECT * FROM {StreamName} EMIT CHANGES LIMIT 1;";
        var queryPayload = JsonSerializer.Serialize(new { ksql = querySql });
        var queryContent = new StringContent(queryPayload, Encoding.UTF8, "application/json");
        var queryResponse = await client.PostAsync($"{KsqlDbUrl}/query", queryContent);
        queryResponse.EnsureSuccessStatusCode();

        var result = await queryResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"Query result: {result}");

        // ④ 結果検証（JSONL形式を想定）
        Assert.Contains("test", result); // JSON構文として "name":"test" が含まれるはず
    }
}