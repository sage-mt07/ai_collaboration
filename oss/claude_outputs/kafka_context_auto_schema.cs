// src/Application/KafkaContext.cs - OnModelCreating → スキーマ自動登録フロー統合版

using Kafka.Ksql.Linq.Configuration;
using Kafka.Ksql.Linq.Core.Abstractions;
using Kafka.Ksql.Linq.Core.Context;
using Kafka.Ksql.Linq.Messaging.Consumers;
using Kafka.Ksql.Linq.Query.Pipeline;
using Kafka.Ksql.Linq.Query.Schema;
using Kafka.Ksql.Linq.Serialization.Abstractions;
using Kafka.Ksql.Linq.Serialization.Avro.Management;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConfluentSchemaRegistry = Confluent.SchemaRegistry;

namespace Kafka.Ksql.Linq.Application;

public abstract class KafkaContext : KafkaContextCore
{
    private readonly KafkaProducerManager _producerManager;
    private readonly KafkaConsumerManager _consumerManager;
    private readonly SchemaRegistry _schemaRegistry;
    private readonly IAvroSchemaRegistrationService _schemaRegistrationService;
    private readonly DDLQueryGenerator _ddlGenerator;
    private readonly KsqlDbExecutor? _ksqlDbExecutor;
    private bool _schemasInitialized = false;

    protected KafkaContext() : base()
    {
        var (producerManager, consumerManager, schemaRegistry, registrationService, ddlGenerator, executor) = 
            InitializeServices(null);
        
        _producerManager = producerManager;
        _consumerManager = consumerManager;
        _schemaRegistry = schemaRegistry;
        _schemaRegistrationService = registrationService;
        _ddlGenerator = ddlGenerator;
        _ksqlDbExecutor = executor;

        InitializeWithAutoSchemaRegistration();
    }

    protected KafkaContext(KafkaContextOptions options) : base(options)
    {
        var (producerManager, consumerManager, schemaRegistry, registrationService, ddlGenerator, executor) = 
            InitializeServices(options);
        
        _producerManager = producerManager;
        _consumerManager = consumerManager;
        _schemaRegistry = schemaRegistry;
        _schemaRegistrationService = registrationService;
        _ddlGenerator = ddlGenerator;
        _ksqlDbExecutor = executor;

        InitializeWithAutoSchemaRegistration();
    }

    private (KafkaProducerManager, KafkaConsumerManager, SchemaRegistry, IAvroSchemaRegistrationService, DDLQueryGenerator, KsqlDbExecutor?) 
        InitializeServices(KafkaContextOptions? options)
    {
        var ksqlOptions = Microsoft.Extensions.Options.Options.Create(new KsqlDslOptions());
        
        var producerManager = new KafkaProducerManager(ksqlOptions, null);
        var consumerManager = new KafkaConsumerManager(ksqlOptions, null);

        KsqlDbExecutor? executor = null;
        SchemaRegistry? schemaRegistry = null;
        IAvroSchemaRegistrationService? registrationService = null;

        if (options?.SchemaRegistryClient != null)
        {
            try
            {
                executor = new KsqlDbRestExecutor("http://localhost:8088", null);
                var ddlGenerator = new DDLQueryGenerator(null);
                schemaRegistry = new SchemaRegistry(executor, ddlGenerator, null);
                registrationService = new AvroSchemaRegistrationService(options.SchemaRegistryClient, null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to initialize Kafka/ksqlDB connection. Verify ksqlDB and Schema Registry are running.", ex);
            }
        }

        return (producerManager, consumerManager, 
                schemaRegistry ?? new NoOpSchemaRegistry(), 
                registrationService ?? new NoOpSchemaRegistrationService(),
                new DDLQueryGenerator(null),
                executor);
    }

    private void InitializeWithAutoSchemaRegistration()
    {
        try
        {
            ConfigureModel();
            
            if (!_schemasInitialized)
            {
                Task.Run(async () => await InitializeSchemasAsync()).GetAwaiter().GetResult();
                _schemasInitialized = true;
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Schema registration failed during context initialization. Check Kafka/ksqlDB connectivity.", ex);
        }
    }

    private async Task InitializeSchemasAsync()
    {
        var entityModels = GetEntityModels();
        var avroConfigurations = ConvertToAvroConfigurations(entityModels);

        await _schemaRegistrationService.RegisterAllSchemasAsync(avroConfigurations);

        foreach (var (entityType, entityModel) in entityModels)
        {
            await _schemaRegistry.RegisterSchemaAsync(entityType, entityModel);
        }
    }

    protected override IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel)
    {
        return new EventSetWithServices<T>(this, entityModel);
    }

    internal KafkaProducerManager GetProducerManager() => _producerManager;
    internal KafkaConsumerManager GetConsumerManager() => _consumerManager;

    internal IReadOnlyDictionary<Type, AvroEntityConfiguration> ConvertToAvroConfigurations(
        Dictionary<Type, EntityModel> entityModels)
    {
        var avroConfigs = new Dictionary<Type, AvroEntityConfiguration>();

        foreach (var kvp in entityModels)
        {
            var entityModel = kvp.Value;
            var avroConfig = new AvroEntityConfiguration(entityModel.EntityType)
            {
                TopicName = entityModel.TopicAttribute?.TopicName,
                KeyProperties = entityModel.KeyProperties
            };

            avroConfigs[kvp.Key] = avroConfig;
        }

        return avroConfigs;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _producerManager?.Dispose();
            _consumerManager?.Dispose();
            _schemaRegistry?.Dispose();
            _ksqlDbExecutor?.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _producerManager?.Dispose();
        _consumerManager?.Dispose();
        _schemaRegistry?.Dispose();
        _ksqlDbExecutor?.Dispose();

        await base.DisposeAsyncCore();
    }

    public override string ToString()
    {
        var schemaStatus = _schemasInitialized ? "スキーマ登録済み" : "スキーマ未登録";
        return $"{base.ToString()} [統合版: {schemaStatus}]";
    }
}

internal class EventSetWithServices<T> : EventSet<T> where T : class
{
    private readonly KafkaContext _kafkaContext;

    public EventSetWithServices(KafkaContext context, EntityModel entityModel)
        : base(context, entityModel)
    {
        _kafkaContext = context;
    }

    protected override async Task SendEntityAsync(T entity, CancellationToken cancellationToken)
    {
        try
        {
            var producerManager = _kafkaContext.GetProducerManager();

            var context = new KafkaMessageContext
            {
                MessageId = Guid.NewGuid().ToString(),
                Tags = new Dictionary<string, object>
                {
                    ["entity_type"] = typeof(T).Name,
                    ["method"] = "AutoSchema.SendEntityAsync"
                }
            };

            await producerManager.SendAsync(entity, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"自動スキーマ登録: Entity送信失敗 - {typeof(T).Name}", ex);
        }
    }
}

internal class NoOpSchemaRegistry : SchemaRegistry
{
    public NoOpSchemaRegistry() : base(new NoOpKsqlDbExecutor(), new DDLQueryGenerator(), null) { }
}

internal class NoOpSchemaRegistrationService : IAvroSchemaRegistrationService
{
    public Task RegisterAllSchemasAsync(IReadOnlyDictionary<Type, AvroEntityConfiguration> configurations)
    {
        return Task.CompletedTask;
    }

    public Task<AvroSchemaInfo> GetSchemaInfoAsync<T>() where T : class
    {
        throw new NotSupportedException("Schema Registry not configured");
    }

    public AvroSchemaInfo GetSchemaInfoAsync(Type entityType)
    {
        throw new NotSupportedException("Schema Registry not configured");
    }

    public Task<List<AvroSchemaInfo>> GetAllRegisteredSchemasAsync()
    {
        return Task.FromResult(new List<AvroSchemaInfo>());
    }
}

internal class NoOpKsqlDbExecutor : KsqlDbExecutor
{
    public NoOpKsqlDbExecutor() : base(null) { }

    public override void ExecuteDDL(string ddlQuery) { }
    public override Task ExecuteDDLAsync(string ddlQuery) => Task.CompletedTask;
    public override Task<List<T>> ExecutePullQueryAsync<T>(string query) => Task.FromResult(new List<T>());
    public override Task<List<T>> ExecutePushQueryAsync<T>(string query) => Task.FromResult(new List<T>());
    public override Task StopAllQueriesAsync() => Task.CompletedTask;
    public override void Dispose() { }
}

internal class KsqlDbRestExecutor : KsqlDbExecutor
{
    private readonly string _ksqlDbUrl;

    public KsqlDbRestExecutor(string ksqlDbUrl, ILoggerFactory? loggerFactory) : base(loggerFactory)
    {
        _ksqlDbUrl = ksqlDbUrl;
    }

    public override void ExecuteDDL(string ddlQuery)
    {
        Task.Run(async () => await ExecuteDDLAsync(ddlQuery)).GetAwaiter().GetResult();
    }

    public override async Task ExecuteDDLAsync(string ddlQuery)
    {
        try
        {
            using var client = new Query.Ksql.KsqlDbRestApiClient(_ksqlDbUrl);
            await client.ExecuteStatementAsync(ddlQuery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DDL実行失敗: {Query}", ddlQuery);
            throw;
        }
    }

    public override async Task<List<T>> ExecutePullQueryAsync<T>(string query)
    {
        try
        {
            using var client = new Query.Ksql.KsqlDbRestApiClient(_ksqlDbUrl);
            var response = await client.ExecuteQueryAsync(query);
            
            var results = new List<T>();
            foreach (var row in response.Rows)
            {
                var entity = ConvertRowToEntity<T>(row);
                if (entity != null)
                    results.Add(entity);
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pull Query実行失敗: {Query}", query);
            throw;
        }
    }

    public override async Task<List<T>> ExecutePushQueryAsync<T>(string query)
    {
        var results = new List<T>();
        
        try
        {
            using var client = new Query.Ksql.KsqlDbRestApiClient(_ksqlDbUrl);
            await client.ExecuteStreamingQueryAsync(query, async (row) =>
            {
                var entity = ConvertRowToEntity<T>(row);
                if (entity != null)
                    results.Add(entity);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push Query実行失敗: {Query}", query);
            throw;
        }

        return results;
    }

    public override Task StopAllQueriesAsync()
    {
        return Task.CompletedTask;
    }

    private T? ConvertRowToEntity<T>(Dictionary<string, object> row) where T : class
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(row);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "行データ変換失敗: {Type}", typeof(T).Name);
            return null;
        }
    }

    public override void Dispose()
    {
        // リソース解放処理
    }
}