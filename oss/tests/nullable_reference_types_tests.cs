using System;
using Xunit;
using KsqlDsl.Attributes;
using KsqlDsl.Options;
using KsqlDsl.Configuration;

namespace KsqlDsl.Tests.NullableReferenceTypes
{
    /// <summary>
    /// Nullable Reference Types対応の確認テスト
    /// C# 8.0 nullable reference types機能との統合を検証
    /// </summary>
    public class NullableReferenceTypesTests
    {
        #region TopicAttribute Null Safety Tests

        [Fact]
        public void TopicAttribute_Constructor_WithNullTopicName_Should_ThrowArgumentException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentException>(() => new TopicAttribute(null!));
        }

        [Fact]
        public void TopicAttribute_Constructor_WithEmptyTopicName_Should_ThrowArgumentException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentException>(() => new TopicAttribute(""));
            Assert.Throws<ArgumentException>(() => new TopicAttribute("   "));
        }

        [Fact]
        public void TopicAttribute_Description_CanBeNull()
        {
            // Arrange
            var attribute = new TopicAttribute("test-topic");

            // Act
            attribute.Description = null;

            // Assert
            Assert.Null(attribute.Description);
            // ToString()がnullを適切に処理することを確認
            var result = attribute.ToString();
            Assert.Contains("test-topic", result);
            Assert.DoesNotContain("null", result);
        }

        [Fact]
        public void TopicAttribute_NullableProperties_HandleCorrectly()
        {
            // Arrange
            var attribute = new TopicAttribute("test-topic");

            // Act - nullable propertiesにnullを設定
            attribute.MaxMessageBytes = null;
            attribute.SegmentBytes = null;
            attribute.Description = null;

            // Assert - ToKafkaTopicConfig()がnullを適切に処理することを確認
            var config = attribute.ToKafkaTopicConfig();
            
            Assert.DoesNotContain("max.message.bytes", config.Keys);
            Assert.DoesNotContain("segment.bytes", config.Keys);
            Assert.Contains("cleanup.policy", config.Keys);
            Assert.Contains("retention.ms", config.Keys);
        }

        #endregion

        #region KafkaContextOptionsBuilder Null Safety Tests

        [Fact]
        public void KafkaContextOptionsBuilder_UseKafka_WithNullConnectionString_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseKafka(null!));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_UseSchemaRegistry_WithNullUrl_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseSchemaRegistry(null!));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_UseConsumerGroup_WithNullGroupId_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseConsumerGroup(null!));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_ConfigureProducer_WithNullKey_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.ConfigureProducer(null!, "value"));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_ConfigureProducer_WithNullValue_Should_ThrowArgumentNullException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.ConfigureProducer("key", null!));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_ConfigureConsumer_WithNullKey_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.ConfigureConsumer(null!, "value"));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_ConfigureConsumer_WithNullValue_Should_ThrowArgumentNullException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.ConfigureConsumer("key", null!));
        }

        [Fact]
        public void KafkaContextOptionsBuilder_OverrideTopicOption_WithNullConfig_Should_ThrowArgumentNullException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => builder.OverrideTopicOption<TestEntity>(null!));
        }

        #endregion

        #region KafkaContextOptions Null Safety Tests

        [Fact]
        public void KafkaContextOptions_ToString_WithNullValues_Should_HandleCorrectly()
        {
            // Arrange
            var options = new KafkaContextOptions
            {
                ConnectionString = null,
                SchemaRegistryUrl = null,
                ConsumerGroupId = null
            };

            // Act
            var result = options.ToString();

            // Assert
            Assert.Contains("未設定", result);
            Assert.DoesNotContain("null", result);
        }

        [Fact]
        public void KafkaContextOptions_Clone_Should_HandleNullValues()
        {
            // Arrange
            var options = new KafkaContextOptions
            {
                ConnectionString = null,
                SchemaRegistryUrl = null,
                ConsumerGroupId = null
            };

            // Act
            var cloned = options.Clone();

            // Assert
            Assert.Null(cloned.ConnectionString);
            Assert.Null(cloned.SchemaRegistryUrl);
            Assert.Null(cloned.ConsumerGroupId);
            Assert.NotSame(options.ProducerConfig, cloned.ProducerConfig);
            Assert.NotSame(options.ConsumerConfig, cloned.ConsumerConfig);
        }

        #endregion

        #region Integration Tests

        [Fact]
        public void CompleteWorkflow_WithNullableValues_Should_WorkCorrectly()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act - nullable値を適切に扱う完全なワークフロー
            var options = builder
                .UseKafka("localhost:9092")
                .UseSchemaRegistry("http://localhost:8081") // non-null
                .UseConsumerGroup("test-group") // non-null
                .EnableDebugLogging()
                .Build();

            // Assert
            Assert.NotNull(options.ConnectionString);
            Assert.NotNull(options.SchemaRegistryUrl);
            Assert.NotNull(options.ConsumerGroupId);
            Assert.True(options.EnableDebugLogging);
        }

        [Fact]
        public void CompleteWorkflow_WithMinimalConfiguration_Should_WorkCorrectly()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act - 最小限の設定（nullable値はnullのまま）
            var options = builder
                .UseKafka("localhost:9092") // 必須設定のみ
                .Build();

            // Assert
            Assert.NotNull(options.ConnectionString);
            Assert.Null(options.SchemaRegistryUrl); // nullableなのでnullでOK
            Assert.Null(options.ConsumerGroupId); // nullableなのでnullでOK
        }

        #endregion

        #region Test Entity

        [Topic("test-events")]
        public class TestEntity
        {
            [Key]
            public int Id { get; set; }
            
            public string Name { get; set; } = string.Empty; // non-nullable
            
            public string? Description { get; set; } // nullable
        }

        #endregion
    }
}