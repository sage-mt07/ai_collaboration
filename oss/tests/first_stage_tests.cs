using KsqlDsl.Attributes;
using KsqlDsl.Configuration;
using KsqlDsl.Options;
using KsqlDsl.Validation;
using System;
using Xunit;

namespace KsqlDsl.Tests.FirstStage
{
    #region Test Entities

    /// <summary>
    /// 正常な設定を持つテストエンティティ
    /// </summary>
    [Topic("trade-events", PartitionCount = 3, ReplicationFactor = 2, RetentionMs = 259200000, 
           Compaction = true, DeadLetterQueue = true, Description = "FX取引イベントストリーム")]
    public class TradeEvent
    {
        [Key]
        public long TradeId { get; set; }

        [MaxLength(12)]
        public string Symbol { get; set; } = string.Empty;

        [DefaultValue(0)]
        public decimal Amount { get; set; }

        public DateTime TradeTime { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// 属性未定義のテストエンティティ
    /// </summary>
    public class MissingAttributesEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// 複合キーを持つテストエンティティ
    /// </summary>
    [Topic("composite-key-events")]
    public class CompositeKeyEntity
    {
        [Key(0)]
        public string PartitionKey { get; set; } = string.Empty;

        [Key(1)]
        public string SortKey { get; set; } = string.Empty;

        public string Data { get; set; } = string.Empty;
    }

    /// <summary>
    /// 無効な設定を持つテストエンティティ
    /// </summary>
    [Topic("invalid-topic", PartitionCount = -1, ReplicationFactor = 0, RetentionMs = -1000)]
    public class InvalidTopicEntity
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// Key順序重複エンティティ
    /// </summary>
    [Topic("duplicate-order-topic")]
    public class DuplicateKeyOrderEntity
    {
        [Key(0)]
        public string Key1 { get; set; } = string.Empty;

        [Key(0)] // 重複したOrder
        public string Key2 { get; set; } = string.Empty;
    }

    #endregion

    #region TopicAttribute Tests

    public class TopicAttributeTests
    {
        [Fact]
        public void TopicAttribute_Constructor_Should_SetTopicNameCorrectly()
        {
            // Arrange & Act
            var attribute = new TopicAttribute("test-topic");

            // Assert
            Assert.Equal("test-topic", attribute.TopicName);
            Assert.Equal(1, attribute.PartitionCount);
            Assert.Equal(1, attribute.ReplicationFactor);
            Assert.Equal(604800000, attribute.RetentionMs); // 7 days default
            Assert.False(attribute.Compaction);
            Assert.False(attribute.DeadLetterQueue);
        }

        [Fact]
        public void TopicAttribute_Constructor_WithEmptyTopicName_Should_ThrowArgumentException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => new TopicAttribute(""));
            Assert.Throws<ArgumentException>(() => new TopicAttribute("   "));
        }

        [Fact]
        public void TopicAttribute_ToKafkaTopicConfig_Should_GenerateCorrectConfig()
        {
            // Arrange
            var attribute = new TopicAttribute("test-topic")
            {
                Compaction = true,
                RetentionMs = 3600000,
                MaxMessageBytes = 1000000,
                SegmentBytes = 104857600
            };

            // Act
            var config = attribute.ToKafkaTopicConfig();

            // Assert
            Assert.Equal("compact", config["cleanup.policy"]);
            Assert.Equal(3600000L, config["retention.ms"]);
            Assert.Equal(1000000, config["max.message.bytes"]);
            Assert.Equal(104857600L, config["segment.bytes"]);
        }

        [Fact]
        public void TopicAttribute_ToString_Should_GenerateExpectedString()
        {
            // Arrange
            var attribute = new TopicAttribute("test-topic")
            {
                Description = "Test description"
            };

            // Act
            var result = attribute.ToString();

            // Assert
            Assert.Contains("test-topic", result);
            Assert.Contains("Test description", result);
            Assert.Contains("Partitions: 1", result);
            Assert.Contains("Replicas: 1", result);
        }
    }

    #endregion

    #region KeyAttribute Tests

    public class KeyAttributeTests
    {
        [Fact]
        public void KeyAttribute_DefaultConstructor_Should_SetDefaultValues()
        {
            // Arrange & Act
            var attribute = new KeyAttribute();

            // Assert
            Assert.Equal(0, attribute.Order);
            Assert.Null(attribute.Encoding);
        }

        [Fact]
        public void KeyAttribute_ConstructorWithOrder_Should_SetOrderCorrectly()
        {
            // Arrange & Act
            var attribute = new KeyAttribute(5);

            // Assert
            Assert.Equal(5, attribute.Order);
        }

        [Fact]
        public void KeyAttribute_ToString_Should_GenerateExpectedString()
        {
            // Arrange
            var attribute = new KeyAttribute(2) { Encoding = "UTF-8" };

            // Act
            var result = attribute.ToString();

            // Assert
            Assert.Contains("Order: 2", result);
            Assert.Contains("Encoding: UTF-8", result);
        }
    }

    #endregion

    #region MaxLengthAttribute Tests

    public class MaxLengthAttributeTests
    {
        [Fact]
        public void MaxLengthAttribute_Constructor_Should_SetLengthCorrectly()
        {
            // Arrange & Act
            var attribute = new MaxLengthAttribute(50);

            // Assert
            Assert.Equal(50, attribute.Length);
        }

        [Fact]
        public void MaxLengthAttribute_Constructor_WithZeroOrNegative_Should_ThrowArgumentException()
        {
            // Arrange, Act & Assert
            Assert.Throws<ArgumentException>(() => new MaxLengthAttribute(0));
            Assert.Throws<ArgumentException>(() => new MaxLengthAttribute(-1));
        }

        [Fact]
        public void MaxLengthAttribute_ToString_Should_GenerateExpectedString()
        {
            // Arrange
            var attribute = new MaxLengthAttribute(100);

            // Act
            var result = attribute.ToString();

            // Assert
            Assert.Equal("MaxLength: 100", result);
        }
    }

    #endregion

    #region DefaultValueAttribute Tests

    public class DefaultValueAttributeTests
    {
        [Fact]
        public void DefaultValueAttribute_Constructor_Should_SetValueCorrectly()
        {
            // Arrange & Act
            var attribute = new DefaultValueAttribute(42);

            // Assert
            Assert.Equal(42, attribute.Value);
        }

        [Fact]
        public void DefaultValueAttribute_Constructor_WithNull_Should_AllowNull()
        {
            // Arrange & Act
            var attribute = new DefaultValueAttribute(null);

            // Assert
            Assert.Null(attribute.Value);
        }

        [Fact]
        public void DefaultValueAttribute_ToString_Should_GenerateExpectedString()
        {
            // Arrange
            var attribute1 = new DefaultValueAttribute("test");
            var attribute2 = new DefaultValueAttribute(null);

            // Act
            var result1 = attribute1.ToString();
            var result2 = attribute2.ToString();

            // Assert
            Assert.Equal("DefaultValue: test", result1);
            Assert.Equal("DefaultValue: null", result2);
        }
    }

    #endregion

    #region ValidationService Tests

    public class ValidationServiceTests
    {
        [Fact]
        public void ValidationService_StrictMode_ValidEntity_Should_ReturnSuccess()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(TradeEvent));

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }

        [Fact]
        public void ValidationService_StrictMode_MissingTopicAttribute_Should_ReturnError()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(MissingAttributesEntity));

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("[Topic]属性がありません"));
        }

        [Fact]
        public void ValidationService_StrictMode_MissingKeyAttribute_Should_ReturnError()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(MissingAttributesEntity));

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("[Key]属性を持つプロパティがありません"));
        }

        [Fact]
        public void ValidationService_RelaxedMode_MissingAttributes_Should_ReturnWarningsWithAutoCompletion()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Relaxed);

            // Act
            var result = service.ValidateEntity(typeof(MissingAttributesEntity));

            // Assert
            Assert.True(result.IsValid); // Relaxedモードでは有効
            Assert.Contains(result.Warnings, w => w.Contains("本番運用非推奨"));
            Assert.Contains(result.AutoCompletedSettings, s => s.Contains("auto-generated"));
        }

        [Fact]
        public void ValidationService_InvalidTopicSettings_Should_ReturnErrors()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(InvalidTopicEntity));

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("PartitionCount"));
            Assert.Contains(result.Errors, e => e.Contains("ReplicationFactor"));
            Assert.Contains(result.Errors, e => e.Contains("RetentionMs"));
        }

        [Fact]
        public void ValidationService_DuplicateKeyOrder_Should_ReturnError()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(DuplicateKeyOrderEntity));

            // Assert
            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.Contains("同じOrder値"));
        }

        [Fact]
        public void ValidationService_CompositeKey_Should_ValidateSuccessfully()
        {
            // Arrange
            var service = new ValidationService(ValidationMode.Strict);

            // Act
            var result = service.ValidateEntity(typeof(CompositeKeyEntity));

            // Assert
            Assert.True(result.IsValid);
            Assert.Empty(result.Errors);
        }
    }

    #endregion

    #region TopicOverrideService Tests

    public class TopicOverrideServiceTests
    {
        [Fact]
        public void TopicOverrideService_OverrideTopicOption_Should_RegisterOverride()
        {
            // Arrange
            var service = new TopicOverrideService();
            var overrideConfig = new TopicOverride
            {
                PartitionCount = 10,
                RetentionMs = 3600000,
                Reason = "Production scaling"
            };

            // Act
            service.OverrideTopicOption<TradeEvent>(overrideConfig);

            // Assert
            var overrides = service.GetAllOverrides();
            Assert.Single(overrides);
            Assert.True(overrides.ContainsKey(typeof(TradeEvent)));
            Assert.Equal(10, overrides[typeof(TradeEvent)].PartitionCount);
            Assert.Equal("Production scaling", overrides[typeof(TradeEvent)].Reason);
        }

        [Fact]
        public void TopicOverrideService_OverrideTopicOptionSimple_Should_RegisterOverride()
        {
            // Arrange
            var service = new TopicOverrideService();

            // Act
            service.OverrideTopicOption<TradeEvent>(partitionCount: 5, retentionMs: 1800000, reason: "Test override");

            // Assert
            var overrides = service.GetAllOverrides();
            Assert.Single(overrides);
            var config = overrides[typeof(TradeEvent)];
            Assert.Equal(5, config.PartitionCount);
            Assert.Equal(1800000, config.RetentionMs);
            Assert.Equal("Test override", config.Reason);
        }

        [Fact]
        public void TopicOverrideService_GetMergedTopicConfig_WithoutOverride_Should_UseAttributeValues()
        {
            // Arrange
            var service = new TopicOverrideService();

            // Act
            var config = service.GetMergedTopicConfig(typeof(TradeEvent));

            // Assert
            Assert.Equal("trade-events", config.TopicName);
            Assert.Equal(3, config.PartitionCount);
            Assert.Equal(2, config.ReplicationFactor);
            Assert.Equal(259200000, config.RetentionMs);
            Assert.True(config.Compaction);
            Assert.True(config.DeadLetterQueue);
            Assert.False(config.HasOverride);
        }

        [Fact]
        public void TopicOverrideService_GetMergedTopicConfig_WithOverride_Should_UseOverrideValues()
        {
            // Arrange
            var service = new TopicOverrideService();
            service.OverrideTopicOption<TradeEvent>(partitionCount: 20, retentionMs: 7200000, reason: "Production settings");

            // Act
            var config = service.GetMergedTopicConfig(typeof(TradeEvent));

            // Assert
            Assert.Equal("trade-events", config.TopicName); // From attribute
            Assert.Equal(20, config.PartitionCount); // From override
            Assert.Equal(2, config.ReplicationFactor); // From attribute
            Assert.Equal(7200000, config.RetentionMs); // From override
            Assert.True(config.HasOverride);
            Assert.Equal("Production settings", config.OverrideReason);
        }

        [Fact]
        public void TopicOverrideService_ToFinalKafkaTopicConfig_Should_GenerateCorrectConfig()
        {
            // Arrange
            var service = new TopicOverrideService();
            service.OverrideTopicOption<TradeEvent>(partitionCount: 15, retentionMs: 1800000);
            var mergedConfig = service.GetMergedTopicConfig(typeof(TradeEvent));

            // Act
            var kafkaConfig = mergedConfig.ToFinalKafkaTopicConfig();

            // Assert
            Assert.Equal("compact", kafkaConfig["cleanup.policy"]); // From attribute (Compaction = true)
            Assert.Equal(1800000L, kafkaConfig["retention.ms"]); // From override
        }

        [Fact]
        public void TopicOverrideService_RemoveOverride_Should_RemoveOverride()
        {
            // Arrange
            var service = new TopicOverrideService();
            service.OverrideTopicOption<TradeEvent>(partitionCount: 10);

            // Act
            service.RemoveOverride<TradeEvent>();

            // Assert
            var overrides = service.GetAllOverrides();
            Assert.Empty(overrides);
        }

        [Fact]
        public void TopicOverrideService_ClearAllOverrides_Should_ClearAllOverrides()
        {
            // Arrange
            var service = new TopicOverrideService();
            service.OverrideTopicOption<TradeEvent>(partitionCount: 10);
            service.OverrideTopicOption<CompositeKeyEntity>(partitionCount: 5);

            // Act
            service.ClearAllOverrides();

            // Assert
            var overrides = service.GetAllOverrides();
            Assert.Empty(overrides);
        }
    }

    #endregion

    #region KafkaContextOptionsBuilder Tests

    public class KafkaContextOptionsBuilderTests
    {
        [Fact]
        public void KafkaContextOptionsBuilder_UseKafka_Should_SetConnectionString()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            var result = builder.UseKafka("localhost:9092");

            // Assert
            Assert.Same(builder, result);
            var options = builder.GetOptions();
            Assert.Equal("localhost:9092", options.ConnectionString);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_UseSchemaRegistry_Should_SetSchemaRegistryUrl()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            builder.UseSchemaRegistry("http://localhost:8081");

            // Assert
            var options = builder.GetOptions();
            Assert.Equal("http://localhost:8081", options.SchemaRegistryUrl);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_EnableRelaxedValidation_Should_SetRelaxedMode()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            builder.EnableRelaxedValidation();

            // Assert
            var options = builder.GetOptions();
            Assert.Equal(ValidationMode.Relaxed, options.ValidationMode);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_EnableStrictValidation_Should_SetStrictMode()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            builder.EnableStrictValidation();

            // Assert
            var options = builder.GetOptions();
            Assert.Equal(ValidationMode.Strict, options.ValidationMode);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_OverrideTopicOption_Should_ConfigureOverride()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            builder.OverrideTopicOption<TradeEvent>(partitionCount: 15, reason: "Performance");

            // Assert
            var options = builder.GetOptions();
            var overrides = options.TopicOverrideService.GetAllOverrides();
            Assert.Single(overrides);
            Assert.Equal(15, overrides[typeof(TradeEvent)].PartitionCount);
            Assert.Equal("Performance", overrides[typeof(TradeEvent)].Reason);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_FluentAPI_Should_ChainCorrectly()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act
            var result = builder
                .UseKafka("localhost:9092")
                .UseSchemaRegistry("http://localhost:8081")
                .EnableRelaxedValidation()
                .UseConsumerGroup("test-group")
                .ConfigureProducer("acks", "all")
                .ConfigureConsumer("auto.offset.reset", "earliest")
                .EnableDebugLogging()
                .DisableAutoTopicCreation()
                .DisableAutoSchemaRegistration();

            // Assert
            Assert.Same(builder, result);
            var options = builder.GetOptions();
            Assert.Equal("localhost:9092", options.ConnectionString);
            Assert.Equal("http://localhost:8081", options.SchemaRegistryUrl);
            Assert.Equal(ValidationMode.Relaxed, options.ValidationMode);
            Assert.Equal("test-group", options.ConsumerGroupId);
            Assert.Equal("all", options.ProducerConfig["acks"]);
            Assert.Equal("earliest", options.ConsumerConfig["auto.offset.reset"]);
            Assert.True(options.EnableDebugLogging);
            Assert.False(options.EnableAutoTopicCreation);
            Assert.False(options.EnableAutoSchemaRegistration);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_Build_WithoutKafka_Should_ThrowInvalidOperationException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());
            Assert.Contains("Kafka接続文字列が設定されていません", exception.Message);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_Build_WithKafka_Should_ReturnValidOptions()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();
            builder.UseKafka("localhost:9092");

            // Act
            var options = builder.Build();

            // Assert
            Assert.NotNull(options);
            Assert.Equal("localhost:9092", options.ConnectionString);
        }

        [Fact]
        public void KafkaContextOptionsBuilder_UseKafka_WithEmptyString_Should_ThrowArgumentException()
        {
            // Arrange
            var builder = new KafkaContextOptionsBuilder();

            // Act & Assert
            Assert.Throws<ArgumentException>(() => builder.UseKafka(""));
            Assert.Throws<ArgumentException>(() => builder.UseKafka("   "));
        }
    }

    #endregion
}