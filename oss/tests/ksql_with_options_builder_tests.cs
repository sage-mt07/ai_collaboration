
using System;
using KsqlDsl.Ksql;
using Xunit;

namespace KsqlDsl.Tests
{
    public class KsqlWithOptionsBuilderTests
    {
        [Fact]
        public void Build_WithAllOptions_Should_GenerateExpectedWithClause()
        {
            var options = new KsqlWithOptions
            {
                TopicName = "orders",
                KeyFormat = "AVRO",
                ValueFormat = "JSON",
                Partitions = 3,
                Replicas = 2
            };

            options.AdditionalOptions["KAFKA_TOPIC_NAME"] = "orders_topic";

            var result = options.BuildWithClause();
			const string expected = "WITH (KAFKA_TOPIC='orders', KEY_FORMAT='AVRO', VALUE_FORMAT='JSON', PARTITIONS=6, REPLICAS=3, TIMESTAMP='OrderTimestamp')";
		    Assert.Equal(expected, clause);
        }
    }
}
