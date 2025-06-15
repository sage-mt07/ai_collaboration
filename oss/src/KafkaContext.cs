using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl;

public abstract class KafkaContext : IDisposable, IAsyncDisposable
{
    private readonly Lazy<ModelBuilder> _modelBuilder;
    private readonly Dictionary<Type, object> _eventSets = new();
    private readonly Lazy<KafkaProducerService> _producerService;
    private readonly Lazy<KafkaConsumerService> _consumerService;
    private AvroSchemaRegistrationService? _schemaRegistrationService;
    private bool _disposed = false;
    private bool _modelBuilt = false;

    public KafkaContextOptions Options { get; private set; }

    protected KafkaContext()
    {
        var optionsBuilder = new KafkaContextOptionsBuilder();
        OnConfiguring(optionsBuilder);
        Options = optionsBuilder.Build();

        _modelBuilder = new Lazy<ModelBuilder>(() =>
        {
            var modelBuilder = new ModelBuilder(Options.ValidationMode);

            if (Options.EnableAutoSchemaRegistration)
            {
                _schemaRegistrationService = new AvroSchemaRegistrationService(
                    Options.CustomSchemaRegistryClient,
                    Options.ValidationMode,
                    Options.EnableDebugLogging);

                modelBuilder.SetSchemaRegistrationService(_schemaRegistrationService);
            }

            OnModelCreating(modelBuilder);

            _ = Task.Run(async () =>
            {
                try
                {
                    await modelBuilder.BuildAsync();
                }
                catch (Exception ex)
                {
                    if (Options.EnableDebugLogging)
                        Console.WriteLine($"[ERROR] Avroスキーマ自動登録エラー: {ex.Message}");
                }
            });

            modelBuilder.Build();
            return modelBuilder;
        });

        _producerService = new Lazy<KafkaProducerService>(() => new KafkaProducerService(Options));
        _consumerService = new Lazy<KafkaConsumerService>(() => new KafkaConsumerService(Options));

        InitializeEventSets();
    }

    protected KafkaContext(KafkaContextOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));

        _modelBuilder = new Lazy<ModelBuilder>(() =>
        {
            var modelBuilder = new ModelBuilder(Options.ValidationMode);

            if (Options.EnableAutoSchemaRegistration)
            {
                _schemaRegistrationService = new AvroSchemaRegistrationService(
                    Options.CustomSchemaRegistryClient,
                    Options.ValidationMode,
                    Options.EnableDebugLogging);

                modelBuilder.SetSchemaRegistrationService(_schemaRegistrationService);
            }

            OnModelCreating(modelBuilder);

            _ = Task.Run(async () =>
            {
                try
                {
                    await modelBuilder.BuildAsync();
                }
                catch (Exception ex)
                {
                    if (Options.EnableDebugLogging)
                        Console.WriteLine($"[ERROR] Avroスキーマ自動登録エラー: {ex.Message}");
                }
            });

            modelBuilder.Build();
            return modelBuilder;
        });

        _producerService = new Lazy<KafkaProducerService>(() => new KafkaProducerService(Options));
        _consumerService = new Lazy<KafkaConsumerService>(() => new KafkaConsumerService(Options));

        InitializeEventSets();
    }

    protected abstract void OnModelCreating(ModelBuilder modelBuilder);
    protected virtual void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder) { }

    internal KafkaProducerService GetProducerService() => _producerService.Value;
    internal KafkaConsumerService GetConsumerService() => _consumerService.Value;

    private void InitializeEventSets()
    {
        var contextType = GetType();
        var eventSetProperties = contextType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsGenericType &&
                       p.PropertyType.GetGenericTypeDefinition() == typeof(EventSet<>))
            .ToArray();

        foreach (var property in eventSetProperties)
        {
            var entityType = property.PropertyType.GetGenericArguments()[0];
        }
    }

    public EventSet<T> Set<T>() where T : class
    {
        var entityType = typeof(T);

        if (_eventSets.TryGetValue(entityType, out var existingSet))
            return (EventSet<T>)existingSet;

        var modelBuilder = _modelBuilder.Value;
        _modelBuilt = true;

        var entityModel = modelBuilder.GetEntityModel<T>();
        if (entityModel == null)
        {
            throw new InvalidOperationException(
                $"エンティティ {entityType.Name} がModelBuilderに登録されていません。" +
                $"OnModelCreating()内でmodelBuilder.Event<{entityType.Name}>()を呼び出してください。");
        }

        var eventSet = new EventSet<T>(this, entityModel);
        _eventSets[entityType] = eventSet;
        return eventSet;
    }

    public object GetEventSet(Type entityType)
    {
        var setMethod = typeof(KafkaContext).GetMethod(nameof(Set))!.MakeGenericMethod(entityType);
        return setMethod.Invoke(this, null)!;
    }

    public ModelBuilder GetModelBuilder() => _modelBuilder.Value;
    public Dictionary<Type, EntityModel> GetEntityModels() => _modelBuilder.Value.GetEntityModels();

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var modelBuilder = _modelBuilder.Value;

        if (Options.EnableDebugLogging)
        {
            Console.WriteLine("[DEBUG] KafkaContext.EnsureCreatedAsync: インフラストラクチャ作成開始");
            Console.WriteLine(modelBuilder.GetModelSummary());
        }

        await Task.Delay(1, cancellationToken);

        if (Options.EnableDebugLogging)
            Console.WriteLine("[DEBUG] KafkaContext.EnsureCreatedAsync: インフラストラクチャ作成完了");
    }

    public void EnsureCreated() => EnsureCreatedAsync().GetAwaiter().GetResult();

    public virtual async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);

        if (Options.EnableDebugLogging)
            Console.WriteLine("[DEBUG] KafkaContext.SaveChangesAsync: Kafka流では通常不要（AddAsync時に即時送信）");

        return 0;
    }

    public virtual int SaveChanges() => SaveChangesAsync().GetAwaiter().GetResult();

    public async Task<List<string>> GetRegisteredSchemasAsync()
    {
        var modelBuilder = _modelBuilder.Value;
        return await modelBuilder.GetRegisteredSchemasAsync();
    }

    public async Task<bool> CheckEntitySchemaCompatibilityAsync<T>() where T : class
    {
        var modelBuilder = _modelBuilder.Value;
        return await modelBuilder.CheckEntitySchemaCompatibilityAsync<T>();
    }

    public string GetDiagnostics()
    {
        var diagnostics = new List<string>
        {
            $"KafkaContext: {GetType().Name}",
            $"Connection: {Options.ConnectionString}",
            $"Schema Registry: {Options.SchemaRegistryUrl}",
            $"Validation Mode: {Options.ValidationMode}",
            $"Consumer Group: {Options.ConsumerGroupId}",
            $"Auto Schema Registration: {Options.EnableAutoSchemaRegistration}",
            $"Model Built: {_modelBuilt}",
            $"EventSets Count: {_eventSets.Count}"
        };

        if (_modelBuilt)
        {
            diagnostics.Add("");
            diagnostics.Add(_modelBuilder.Value.GetModelSummary());
        }

        if (Options.TopicOverrideService.GetAllOverrides().Any())
        {
            diagnostics.Add("");
            diagnostics.Add(Options.TopicOverrideService.GetOverrideSummary());
        }

        return string.Join(Environment.NewLine, diagnostics);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _eventSets.Clear();

            if (_producerService.IsValueCreated)
                _producerService.Value.Dispose();

            if (_consumerService.IsValueCreated)
                _consumerService.Value.Dispose();

            if (Options.EnableDebugLogging)
                Console.WriteLine("[DEBUG] KafkaContext.Dispose: リソース解放完了");

            _disposed = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        Dispose(false);
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_producerService.IsValueCreated)
            _producerService.Value.Dispose();

        if (_consumerService.IsValueCreated)
            _consumerService.Value.Dispose();

        await Task.Delay(1);
    }

    public override string ToString()
    {
        var connectionInfo = !string.IsNullOrEmpty(Options.ConnectionString)
            ? Options.ConnectionString
            : "未設定";
        return $"{GetType().Name} → {connectionInfo}";
    }
}