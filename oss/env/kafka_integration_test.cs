using Xunit;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace KsqlDsl.Tests.Physical
{
    public class KafkaIntegrationTest
    {
        private const string KsqlServerUrl = "http://localhost:8088";

        [Fact(DisplayName = "ksqlDB クエリがエラーなく実行され、Kafka による処理結果が取得できる")]
        public async Task Should_ExecuteKsqlQuerySuccessfully()
        {
            using var client = new HttpClient();

            var ksqlStatement = new
            {
                ksql = "SHOW STREAMS;",
                streamsProperties = new { }
            };

            var content = new StringContent(
                JObject.FromObject(ksqlStatement).ToString(),
                Encoding.UTF8,
                "application/vnd.ksql.v1+json"
            );

            var response = await client.PostAsync($"{KsqlServerUrl}/ksql", content);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            Assert.Contains("statementText", json);
        }
    }
}
