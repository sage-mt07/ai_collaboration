using System;
using System.Threading.Tasks;
using Confluent.Kafka;
using System.Text.Json;

class Program
{
    public static async Task Main(string[] args)
    {
        var config = new ProducerConfig
        {
            // WSL + Docker環境で外部アクセスする場合は9093ポートを使用
            BootstrapServers = "localhost:9093", // 192.168.1.100:9092 から変更
            ClientId = "csharp-producer",
            // 接続タイムアウトの設定を追加（オプション）
            MessageTimeoutMs = 10000,
            RequestTimeoutMs = 5000
        };

        string topic = "test_topic";

        try
        {
            using var producer = new ProducerBuilder<int, string>(config)
                .SetKeySerializer(Serializers.Int32)
                .SetValueSerializer(Serializers.Utf8)
                .SetErrorHandler((_, e) => Console.WriteLine($"Error: {e.Reason}"))
                .Build();

            var messages = new[]
            {
                new { Id = 1, Message = "Hello from C# Kafka!" },
                new { Id = 2, Message = "これはC#から送ったJSONメッセージです" }
            };

            foreach (var msg in messages)
            {
                var jsonValue = JsonSerializer.Serialize(new { id = msg.Id, message = msg.Message });

                var result = await producer.ProduceAsync(topic, new Message<int, string>
                {
                    Key = msg.Id,
                    Value = jsonValue
                });

                Console.WriteLine($"Delivered to {result.TopicPartitionOffset}");
            }

            producer.Flush(TimeSpan.FromSeconds(5));
            Console.WriteLine("メッセージの送信が完了しました。");
        }
        catch (ProduceException<int, string> e)
        {
            Console.WriteLine($"Delivery failed: {e.Error.Reason}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"予期しないエラー: {e.Message}");
        }
    }
}