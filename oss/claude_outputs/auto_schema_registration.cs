// src/Core/Context/KafkaContextCore.cs (修正版)
using Kafka.Ksql.Linq.Core.Abstractions;
using Kafka.Ksql.Linq.Core.Modeling;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Kafka.Ksql.Linq.Core.Context;

public abstract class KafkaContextCore : IKafkaContext
{
    private readonly Dictionary<Type, EntityModel> _entityModels = new();
    private readonly Dictionary<Type, object> _entitySets = new();
    protected readonly KafkaContextOptions Options;
    private bool _disposed = false;
    private bool _schemasInitialized = false;
    private readonly object _initializationLock = new();

    protected KafkaContextCore()
    {
        Options = new KafkaContextOptions();
    }

    protected KafkaContextCore(KafkaContextOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected virtual void OnModelCreating(IModelBuilder modelBuilder) { }

    protected abstract void EnsureSchemaRegistered(Dictionary<Type, EntityModel> entityModels);

    public IEntitySet<T> Set<T>() where T : class
    {
        EnsureSchemasInitialized();
        
        var entityType = typeof(T);

        if (_entitySets.TryGetValue(entityType, out var existingSet))
        {
            return (IEntitySet<T>)existingSet;
        }

        var entityModel = GetOrCreateEntityModel<T>();
        var entitySet = CreateEntitySet<T>(entityModel);
        _entitySets[entityType] = entitySet;

        return entitySet;
    }

    public object GetEventSet(Type entityType)
    {
        EnsureSchemasInitialized();
        
        if (_entitySets.TryGetValue(entityType, out var entitySet))
        {
            return entitySet;
        }

        var entityModel = GetOrCreateEntityModel(entityType);
        var createdSet = CreateEntitySet(entityType, entityModel);
        _entitySets[entityType] = createdSet;

        return createdSet;
    }

    public Dictionary<Type, EntityModel> GetEntityModels()
    {
        EnsureSchemasInitialized();
        return new Dictionary<Type, EntityModel>(_entityModels);
    }

    private void EnsureSchemasInitialized()
    {
        if (_schemasInitialized) return;

        lock (_initializationLock)
        {
            if (_schemasInitialized) return;

            var modelBuilder = new ModelBuilder(Options.ValidationMode);
            OnModelCreating(modelBuilder);
            ApplyModelBuilderSettings(modelBuilder);
            
            EnsureSchemaRegistered(_entityModels);
            
            _schemasInitialized = true;
        }
    }

    protected abstract IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel) where T : class;

    protected virtual object CreateEntitySet(Type entityType, EntityModel entityModel)
    {
        var method = GetType().GetMethod(nameof(CreateEntitySet), 1, new[] { typeof(EntityModel) });
        var genericMethod = method!.MakeGenericMethod(entityType);
        return genericMethod.Invoke(this, new object[] { entityModel })!;
    }

    private void ApplyModelBuilderSettings(ModelBuilder modelBuilder)
    {
        var models = modelBuilder.GetAllEntityModels();
        foreach (var (type, model) in models)
        {
            _entityModels[type] = model;
        }
    }

    private EntityModel GetOrCreateEntityModel<T>() where T : class
    {
        return GetOrCreateEntityModel(typeof(T));
    }

    private EntityModel GetOrCreateEntityModel(Type entityType)
    {
        if (_entityModels.TryGetValue(entityType, out var existingModel))
        {
            return existingModel;
        }

        var entityModel = CreateEntityModelFromType(entityType);
        _entityModels[entityType] = entityModel;
        return entityModel;
    }

    private EntityModel CreateEntityModelFromType(Type entityType)
    {
        var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();
        var allProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var keyProperties = Array.FindAll(allProperties, p => p.GetCustomAttribute<KeyAttribute>() != null);

        var model = new EntityModel
        {
            EntityType = entityType,
            TopicAttribute = topicAttribute,
            AllProperties = allProperties,
            KeyProperties = keyProperties
        };

        var validation = new ValidationResult { IsValid = true };

        if (topicAttribute == null)
        {
            validation.Warnings.Add($"No [Topic] attribute found for {entityType.Name}");
        }

        if (keyProperties.Length == 0)
        {
            validation.Warnings.Add($"No [Key] properties found for {entityType.Name}");
        }

        model.ValidationResult = validation;

        return model;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            foreach (var entitySet in _entitySets.Values)
            {
                if (entitySet is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _entitySets.Clear();
            _entityModels.Clear();
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public virtual async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async System.Threading.Tasks.ValueTask DisposeAsyncCore()
    {
        foreach (var entitySet in _entitySets.Values)
        {
            if (entitySet is System.IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else if (entitySet is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _entitySets.Clear();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    public override string ToString()
    {
        return $"KafkaContextCore: {_entityModels.Count} entities, {_entitySets.Count} sets";
    }
}

// src/Application/KafkaContext.cs (修正版)
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

namespace Kafka.Ksql.Linq.Application;

public abstract class KafkaContext : KafkaContextCore
{
    private readonly KafkaProducerManager _producerManager;
    private readonly KafkaConsumerManager _consumerManager;
    private SchemaRegistry? _schemaRegistry;
    private AvroSchemaRegistrationService? _avroService;

    protected KafkaContext() : base()
    {
        _producerManager = new KafkaProducerManager(
            Microsoft.Extensions.Options.Options.Create(new KsqlDslOptions()),
            null);

        _consumerManager = new KafkaConsumerManager(
            Microsoft.Extensions.Options.Options.Create(new KsqlDslOptions()),
            null);
    }

    protected KafkaContext(KafkaContextOptions options) : base(options)
    {
        _producerManager = new KafkaProducerManager(
            Microsoft.Extensions.Options.Options.Create(new KsqlDslOptions()),
            null);

        _consumerManager = new KafkaConsumerManager(
            Microsoft.Extensions.Options.Options.Create(new KsqlDslOptions()),
            null);
    }

    protected override IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel)
    {
        return new EventSetWithServices<T>(this, entityModel);
    }

    protected override void EnsureSchemaRegistered(Dictionary<Type, EntityModel> entityModels)
    {
        if (entityModels.Count == 0) return;

        try
        {
            InitializeSchemaServices();
            RegisterKsqlSchemas(entityModels);
            RegisterAvroSchemas(entityModels);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Schema registration failed during initialization", ex);
        }
    }

    private void InitializeSchemaServices()
    {
        if (_schemaRegistry != null) return;

        var schemaRegistryClient = CreateSchemaRegistryClient();
        var executor = new SynchronousKsqlDbExecutor();
        var ddlGenerator = new DDLQueryGenerator();
        
        _schemaRegistry = new SchemaRegistry(executor, ddlGenerator);
        _avroService = new AvroSchemaRegistrationService(schemaRegistryClient);
    }

    private void RegisterKsqlSchemas(Dictionary<Type, EntityModel> entityModels)
    {
        foreach (var kvp in entityModels)
        {
            var task = _schemaRegistry!.RegisterSchemaAsync(kvp.Value);
            task.Wait();
        }
    }

    private void RegisterAvroSchemas(Dictionary<Type, EntityModel> entityModels)
    {
        var avroConfigs = ConvertToAvroConfigurations(entityModels);
        var task = _avroService!.RegisterAllSchemasAsync(avroConfigs);
        task.Wait();
    }

    private Confluent.SchemaRegistry.ISchemaRegistryClient CreateSchemaRegistryClient()
    {
        var url = Environment.GetEnvironmentVariable("SCHEMA_REGISTRY_URL") ?? "http://localhost:8081";
        var config = new Confluent.SchemaRegistry.SchemaRegistryConfig { Url = url };
        return new Confluent.SchemaRegistry.CachedSchemaRegistryClient(config);
    }

    internal KafkaProducerManager GetProducerManager() => _producerManager;
    internal KafkaConsumerManager GetConsumerManager() => _consumerManager;

    protected IReadOnlyDictionary<Type, AvroEntityConfiguration> ConvertToAvroConfigurations(
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
        }

        base.Dispose(disposing);
    }

    protected override async System.Threading.Tasks.ValueTask DisposeAsyncCore()
    {
        _producerManager?.Dispose();
        _consumerManager?.Dispose();
        _schemaRegistry?.Dispose();

        await base.DisposeAsyncCore();
    }

    public override string ToString()
    {
        return $"{base.ToString()} [Auto Schema Registration]";
    }
}

// src/Query/Pipeline/SynchronousKsqlDbExecutor.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kafka.Ksql.Linq.Query.Pipeline;

internal class SynchronousKsqlDbExecutor : KsqlDbExecutor
{
    internal SynchronousKsqlDbExecutor() : base(null)
    {
    }

    public override void ExecuteDDL(string ddlQuery)
    {
        if (IsSchemaExistsQuery(ddlQuery))
        {
            return;
        }
        
        Console.WriteLine($"[Schema Registration] {ddlQuery}");
    }

    public override async Task ExecuteDDLAsync(string ddlQuery)
    {
        ExecuteDDL(ddlQuery);
        await Task.CompletedTask;
    }

    public override async Task<List<T>> ExecutePullQueryAsync<T>(string query) where T : class
    {
        await Task.CompletedTask;
        return new List<T>();
    }

    public override async Task<List<T>> ExecutePushQueryAsync<T>(string query) where T : class
    {
        await Task.CompletedTask;
        return new List<T>();
    }

    public override async Task StopAllQueriesAsync()
    {
        await Task.CompletedTask;
    }

    private bool IsSchemaExistsQuery(string ddlQuery)
    {
        return ddlQuery.Contains("IF NOT EXISTS") || 
               ddlQuery.ToUpper().Contains("SHOW STREAMS") || 
               ddlQuery.ToUpper().Contains("SHOW TABLES");
    }

    public override void Dispose()
    {
    }
}