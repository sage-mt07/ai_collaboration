using KsqlDsl.Configuration;
using KsqlDsl.SchemaRegistry;
using KsqlDsl.Validation;
using System.Collections.Generic;

namespace KsqlDsl.Options;

public class KafkaContextOptions
{
    public string? ConnectionString { get; set; }
    public string? SchemaRegistryUrl { get; set; }
    public ValidationMode ValidationMode { get; set; } = ValidationMode.Strict;
    public TopicOverrideService TopicOverrideService { get; set; } = new();
    public string? ConsumerGroupId { get; set; }
    public Dictionary<string, object> ProducerConfig { get; set; } = new();
    public Dictionary<string, object> ConsumerConfig { get; set; } = new();
    public bool EnableDebugLogging { get; set; } = false;
    public bool EnableAutoTopicCreation { get; set; } = true;
    public bool EnableAutoSchemaRegistration { get; set; } = true;

    public bool ForceSchemaRegistration { get; set; } = false;
    public ISchemaRegistryClient? CustomSchemaRegistryClient { get; set; }
    public string? SchemaRegistryUsername { get; set; }
    public string? SchemaRegistryPassword { get; set; }
    public SchemaGenerationOptions? SchemaGenerationOptions { get; set; }

    public KafkaContextOptions Clone()
    {
        return new KafkaContextOptions
        {
            ConnectionString = ConnectionString,
            SchemaRegistryUrl = SchemaRegistryUrl,
            ValidationMode = ValidationMode,
            TopicOverrideService = TopicOverrideService,
            ConsumerGroupId = ConsumerGroupId,
            ProducerConfig = new Dictionary<string, object>(ProducerConfig),
            ConsumerConfig = new Dictionary<string, object>(ConsumerConfig),
            EnableDebugLogging = EnableDebugLogging,
            EnableAutoTopicCreation = EnableAutoTopicCreation,
            EnableAutoSchemaRegistration = EnableAutoSchemaRegistration,
            ForceSchemaRegistration = ForceSchemaRegistration,
            CustomSchemaRegistryClient = CustomSchemaRegistryClient,
            SchemaRegistryUsername = SchemaRegistryUsername,
            SchemaRegistryPassword = SchemaRegistryPassword,
            SchemaGenerationOptions = SchemaGenerationOptions
        };
    }

    public override string ToString()
    {
        var connectionString = ConnectionString ?? "未設定";
        var schemaRegistry = SchemaRegistryUrl ?? "未設定";
        var consumerGroup = ConsumerGroupId ?? "未設定";
        var autoSchemaReg = EnableAutoSchemaRegistration ? "有効" : "無効";

        return $"KafkaContext Options: Kafka={connectionString}, SchemaRegistry={schemaRegistry}, " +
               $"Validation={ValidationMode}, ConsumerGroup={consumerGroup}, AutoSchemaReg={autoSchemaReg}";
    }
}