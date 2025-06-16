using KsqlDsl.Configuration;
using KsqlDsl.Validation;
using System;

namespace KsqlDsl.Options;

public class KafkaContextOptionsBuilder
{
    private readonly KafkaContextOptions _options = new();

    public KafkaContextOptionsBuilder UseKafka(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Kafka接続文字列は必須です", nameof(connectionString));

        _options.ConnectionString = connectionString;
        return this;
    }

    public KafkaContextOptionsBuilder UseSchemaRegistry(string schemaRegistryUrl)
    {
        if (string.IsNullOrWhiteSpace(schemaRegistryUrl))
            throw new ArgumentException("Schema Registry URLは必須です", nameof(schemaRegistryUrl));

        _options.SchemaRegistryUrl = schemaRegistryUrl;
        return this;
    }

    public KafkaContextOptionsBuilder EnableRelaxedValidation()
    {
        _options.ValidationMode = ValidationMode.Relaxed;
        return this;
    }

    public KafkaContextOptionsBuilder EnableStrictValidation()
    {
        _options.ValidationMode = ValidationMode.Strict;
        return this;
    }

    public KafkaContextOptionsBuilder OverrideTopicOption<T>(TopicOverride overrideConfig)
    {
        if (overrideConfig == null)
            throw new ArgumentNullException(nameof(overrideConfig));

        _options.TopicOverrideService.OverrideTopicOption<T>(overrideConfig);
        return this;
    }

    public KafkaContextOptionsBuilder OverrideTopicOption<T>(int? partitionCount = null, int? replicationFactor = null, long? retentionMs = null, string? reason = null)
    {
        _options.TopicOverrideService.OverrideTopicOption<T>(partitionCount, replicationFactor, retentionMs, reason);
        return this;
    }

    public KafkaContextOptionsBuilder UseConsumerGroup(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("コンシューマーグループIDは必須です", nameof(groupId));

        _options.ConsumerGroupId = groupId;
        return this;
    }

    public KafkaContextOptionsBuilder ConfigureProducer(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("設定キーは必須です", nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _options.ProducerConfig[key] = value;
        return this;
    }

    public KafkaContextOptionsBuilder ConfigureConsumer(string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("設定キーは必須です", nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        _options.ConsumerConfig[key] = value;
        return this;
    }

    public KafkaContextOptionsBuilder EnableDebugLogging()
    {
        _options.EnableDebugLogging = true;
        return this;
    }

    public KafkaContextOptionsBuilder DisableAutoTopicCreation()
    {
        _options.EnableAutoTopicCreation = false;
        return this;
    }

    public KafkaContextOptionsBuilder DisableAutoSchemaRegistration()
    {
        _options.EnableAutoSchemaRegistration = false;
        return this;
    }

    public KafkaContextOptions Build()
    {
        // 必須設定の検証
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("Kafka接続文字列が設定されていません。UseKafka()を呼び出してください。");
        }

        return _options.Clone();
    }

    internal KafkaContextOptions GetOptions()
    {
        return _options;
    }
}
