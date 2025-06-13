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
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "orders",
                KeyFormat = "AVRO",
                ValueFormat = "JSON",
                Partitions = 3,
                Replicas = 2
            };

            options.AdditionalOptions["TIMESTAMP"] = "'OrderTimestamp'";

            // Act
            var result = options.BuildWithClause();

            // Assert - 実際の設定値に合わせた期待値
            const string expected = " WITH (KAFKA_TOPIC='orders', KEY_FORMAT='AVRO', VALUE_FORMAT='JSON', PARTITIONS=3, REPLICAS=2, TIMESTAMP='OrderTimestamp')";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithMinimalOptions_Should_GenerateSimpleWithClause()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "orders",
                ValueFormat = "AVRO"
            };

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (KAFKA_TOPIC='orders', VALUE_FORMAT='AVRO')";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithNoOptions_Should_ReturnEmptyString()
        {
            // Arrange
            var options = new KsqlWithOptions();

            // Act
            var result = options.BuildWithClause();

            // Assert
            Assert.Equal("", result);
        }

        [Fact]
        public void Build_WithOnlyAdditionalOptions_Should_GenerateCorrectWithClause()
        {
            // Arrange
            var options = new KsqlWithOptions();
            options.AdditionalOptions["TIMESTAMP"] = "'ROWTIME'";
            options.AdditionalOptions["WRAP_SINGLE_VALUE"] = "false";

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (TIMESTAMP='ROWTIME', WRAP_SINGLE_VALUE=false)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithAllStandardOptions_Should_GenerateCorrectWithClause()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "test-topic",
                KeyFormat = "JSON",
                ValueFormat = "AVRO",
                Partitions = 6,
                Replicas = 3
            };

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (KAFKA_TOPIC='test-topic', KEY_FORMAT='JSON', VALUE_FORMAT='AVRO', PARTITIONS=6, REPLICAS=3)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithMixedOptions_Should_GenerateCorrectWithClause()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "mixed-topic",
                KeyFormat = "AVRO",
                Partitions = 1
            };

            options.AdditionalOptions["TIMESTAMP"] = "'EventTime'";
            options.AdditionalOptions["WRAP_SINGLE_VALUE"] = "true";

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (KAFKA_TOPIC='mixed-topic', KEY_FORMAT='AVRO', PARTITIONS=1, TIMESTAMP='EventTime', WRAP_SINGLE_VALUE=true)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithNullValues_Should_SkipNullOptions()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "orders",
                KeyFormat = null, // null should be skipped
                ValueFormat = "JSON",
                Partitions = null, // null should be skipped
                Replicas = 2
            };

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (KAFKA_TOPIC='orders', VALUE_FORMAT='JSON', REPLICAS=2)";
            Assert.Equal(expected, result);
        }

        [Fact]
        public void Build_WithEmptyStringValues_Should_SkipEmptyOptions()
        {
            // Arrange
            var options = new KsqlWithOptions
            {
                TopicName = "orders",
                KeyFormat = "", // empty should be skipped
                ValueFormat = "JSON"
            };

            // Act
            var result = options.BuildWithClause();

            // Assert
            const string expected = " WITH (KAFKA_TOPIC='orders', VALUE_FORMAT='JSON')";
            Assert.Equal(expected, result);
        }
    }
}