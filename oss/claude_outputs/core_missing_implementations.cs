// src/Configuration/Options/AvroOperationRetrySettings.cs
using System;
using System.Collections.Generic;

namespace KsqlDsl.Configuration.Options
{
    /// <summary>
    /// Avro操作リトライ設定
    /// 設計理由: ResilientAvroSerializerManagerで参照されているが実装が不足
    /// </summary>
    public class AvroOperationRetrySettings
    {
        public AvroRetryPolicy SchemaRegistration { get; set; } = new()
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromMilliseconds(500),
            MaxDelay = TimeSpan.FromSeconds(5),
            BackoffMultiplier = 2.0,
            RetryableExceptions = new List<Type> { typeof(System.Net.Http.HttpRequestException), typeof(TimeoutException) },
            NonRetryableExceptions = new List<Type> { typeof(ArgumentException), typeof(InvalidOperationException) }
        };

        public AvroRetryPolicy SchemaRetrieval { get; set; } = new()
        {
            MaxAttempts = 2,
            InitialDelay = TimeSpan.FromMilliseconds(250),
            MaxDelay = TimeSpan.FromSeconds(2),
            BackoffMultiplier = 1.5,
            RetryableExceptions = new List<Type> { typeof(System.Net.Http.HttpRequestException) },
            NonRetryableExceptions = new List<Type> { typeof(ArgumentException) }
        };

        public AvroRetryPolicy CompatibilityCheck { get; set; } = new()
        {
            MaxAttempts = 2,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            MaxDelay = TimeSpan.FromSeconds(1),
            BackoffMultiplier = 1.5,
            RetryableExceptions = new List<Type> { typeof(System.Net.Http.HttpRequestException) },
            NonRetryableExceptions = new List<Type> { typeof(ArgumentException) }
        };
    }

    /// <summary>
    /// Avroリトライポリシー
    /// </summary>
    public class AvroRetryPolicy
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(500);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(10);
        public double BackoffMultiplier { get; set; } = 2.0;
        public List<Type> RetryableExceptions { get; set; } = new();
        public List<Type> NonRetryableExceptions { get; set; } = new();

        public void Validate()
        {
            if (MaxAttempts <= 0)
                throw new ArgumentException("MaxAttempts must be greater than 0");
            
            if (InitialDelay <= TimeSpan.Zero)
                throw new ArgumentException("InitialDelay must be positive");
            
            if (MaxDelay <= TimeSpan.Zero)
                throw new ArgumentException("MaxDelay must be positive");
            
            if (BackoffMultiplier <= 0)
                throw new ArgumentException("BackoffMultiplier must be positive");
        }
    }
}

// src/Monitoring/Tracing/AvroActivitySource.cs
using System;
using System.Diagnostics;

namespace KsqlDsl.Monitoring.Tracing
{
    /// <summary>
    /// Avro操作用ActivitySource
    /// 設計理由: EnhancedAvroSerializerManagerで参照されているが実装が不足
    /// </summary>
    public static class AvroActivitySource
    {
        private static readonly ActivitySource _activitySource = new("KsqlDsl.Avro", "1.0.0");

        public static Activity? StartSchemaRegistration(string subject)
        {
            return _activitySource.StartActivity("avro.schema.register")
                ?.SetTag("schema.subject", subject);
        }

        public static Activity? StartCacheOperation(string operation, string entityType)
        {
            return _activitySource.StartActivity($"avro.cache.{operation}")
                ?.SetTag("entity.type", entityType);
        }

        public static Activity? StartSerialization(string entityType, string operation)
        {
            return _activitySource.StartActivity($"avro.serialization.{operation}")
                ?.SetTag("entity.type", entityType);
        }

        public static void Dispose()
        {
            _activitySource.Dispose();
        }
    }
}

// src/Core/Context/KafkaContextCore.cs
using KsqlDsl.Core.Abstractions;
using KsqlDsl.Core.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.Core.Context
{
    /// <summary>
    /// Core層KafkaContext基底実装
    /// 設計理由: KafkaContext.csで参照されているが実装が不足
    /// </summary>
    public abstract class KafkaContextCore : IKafkaContext
    {
        private readonly Dictionary<Type, EntityModel> _entityModels = new();
        private readonly Dictionary<Type, object> _entitySets = new();
        protected readonly KafkaContextOptions Options;
        private readonly ILogger<KafkaContextCore> _logger;
        private bool _disposed = false;

        protected KafkaContextCore()
        {
            Options = new KafkaContextOptions();
            _logger = NullLogger<KafkaContextCore>.Instance;
            InitializeEntityModels();
        }

        protected KafkaContextCore(KafkaContextOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = Options.LoggerFactory.CreateLoggerOrNull<KafkaContextCore>();
            InitializeEntityModels();
        }

        public IEntitySet<T> Set<T>() where T : class
        {
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
            if (_entitySets.TryGetValue(entityType, out var entitySet))
            {
                return entitySet;
            }

            var entityModel = GetOrCreateEntityModel(entityType);
            var setType = typeof(IEntitySet<>).MakeGenericType(entityType);
            var createdSet = CreateEntitySet(entityType, entityModel);
            _entitySets[entityType] = createdSet;

            return createdSet;
        }

        public virtual Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Core層では基本実装のみ提供
            return Task.FromResult(0);
        }

        public virtual int SaveChanges()
        {
            // Core層では基本実装のみ提供
            return 0;
        }

        public virtual Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
        {
            // Core層では基本実装のみ提供
            _logger?.LogDebug("KafkaContextCore.EnsureCreatedAsync completed");
            return Task.CompletedTask;
        }

        public virtual void EnsureCreated()
        {
            // Core層では基本実装のみ提供
            _logger?.LogDebug("KafkaContextCore.EnsureCreated completed");
        }

        public Dictionary<Type, EntityModel> GetEntityModels()
        {
            return new Dictionary<Type, EntityModel>(_entityModels);
        }

        public virtual string GetDiagnostics()
        {
            var diagnostics = new List<string>
            {
                "=== KafkaContextCore診断情報 ===",
                $"エンティティモデル数: {_entityModels.Count}",
                $"エンティティセット数: {_entitySets.Count}",
                ""
            };

            if (_entityModels.Count > 0)
            {
                diagnostics.Add("=== エンティティモデル ===");
                foreach (var (type, model) in _entityModels)
                {
                    var status = model.IsValid ? "✅" : "❌";
                    diagnostics.Add($"{status} {type.Name} → {model.GetTopicName()} (Keys: {model.KeyProperties.Length})");
                }
            }

            return string.Join(Environment.NewLine, diagnostics);
        }

        protected virtual IEntitySet<T> CreateEntitySet<T>(EntityModel entityModel) where T : class
        {
            // 具象クラスでオーバーライド
            throw new NotImplementedException("CreateEntitySet must be implemented by concrete class");
        }

        protected virtual object CreateEntitySet(Type entityType, EntityModel entityModel)
        {
            // リフレクションを使用してジェネリックメソッドを呼び出し
            var method = GetType().GetMethod(nameof(CreateEntitySet), 1, new[] { typeof(EntityModel) });
            var genericMethod = method!.MakeGenericMethod(entityType);
            return genericMethod.Invoke(this, new object[] { entityModel })!;
        }

        private void InitializeEntityModels()
        {
            // サブクラスでOnModelCreatingを呼び出すための準備
            _logger?.LogDebug("Entity models initialization started");
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
            var allProperties = entityType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var keyProperties = Array.FindAll(allProperties, p => p.GetCustomAttribute<KeyAttribute>() != null);

            var model = new EntityModel
            {
                EntityType = entityType,
                TopicAttribute = topicAttribute,
                AllProperties = allProperties,
                KeyProperties = keyProperties
            };

            // 基本検証
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

        public virtual async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore();
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            foreach (var entitySet in _entitySets.Values)
            {
                if (entitySet is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (entitySet is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _entitySets.Clear();
            await Task.CompletedTask;
        }

        public override string ToString()
        {
            return $"KafkaContextCore: {_entityModels.Count} entities, {_entitySets.Count} sets";
        }
    }

    /// <summary>
    /// KafkaContextOptions
    /// </summary>
    public class KafkaContextOptions
    {
        public ILoggerFactory? LoggerFactory { get; set; }
        public bool EnableDebugLogging { get; set; } = false;
        public ValidationMode ValidationMode { get; set; } = ValidationMode.Strict;
        public bool EnableAutoSchemaRegistration { get; set; } = true;
        public Confluent.SchemaRegistry.ISchemaRegistryClient? CustomSchemaRegistryClient { get; set; }
        public Configuration.Abstractions.TopicOverrideService TopicOverrideService { get; set; } = new();

        public void Validate()
        {
            // 基本的な検証
            if (EnableAutoSchemaRegistration && CustomSchemaRegistryClient == null)
            {
                throw new InvalidOperationException("CustomSchemaRegistryClient is required when EnableAutoSchemaRegistration is true");
            }
        }
    }
}

// src/Core/Modeling/ModelBuilder.cs
using KsqlDsl.Configuration;
using KsqlDsl.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace KsqlDsl.Core.Modeling
{
    /// <summary>
    /// Core層ModelBuilder
    /// 設計理由: KafkaContext.csで参照されているが実装が不足
    /// </summary>
    public class ModelBuilder
    {
        private readonly Dictionary<Type, EntityModel> _entityModels = new();
        private readonly ValidationMode _validationMode;

        public ModelBuilder(ValidationMode validationMode = ValidationMode.Strict)
        {
            _validationMode = validationMode;
        }

        public EntityModel? GetEntityModel<T>() where T : class
        {
            return GetEntityModel(typeof(T));
        }

        public EntityModel? GetEntityModel(Type entityType)
        {
            _entityModels.TryGetValue(entityType, out var model);
            return model;
        }

        public void AddEntityModel<T>() where T : class
        {
            AddEntityModel(typeof(T));
        }

        public void AddEntityModel(Type entityType)
        {
            if (_entityModels.ContainsKey(entityType))
                return;

            var entityModel = CreateEntityModelFromType(entityType);
            _entityModels[entityType] = entityModel;
        }

        public Dictionary<Type, EntityModel> GetAllEntityModels()
        {
            return new Dictionary<Type, EntityModel>(_entityModels);
        }

        public string GetModelSummary()
        {
            if (_entityModels.Count == 0)
                return "ModelBuilder: No entities configured";

            var summary = new List<string>
            {
                $"ModelBuilder: {_entityModels.Count} entities configured",
                $"Validation Mode: {_validationMode}",
                ""
            };

            foreach (var (entityType, model) in _entityModels.OrderBy(x => x.Key.Name))
            {
                var status = model.IsValid ? "✅" : "❌";
                summary.Add($"{status} {entityType.Name} → {model.GetTopicName()} ({model.StreamTableType}, Keys: {model.KeyProperties.Length})");
                
                if (model.ValidationResult != null && !model.ValidationResult.IsValid)
                {
                    foreach (var error in model.ValidationResult.Errors)
                    {
                        summary.Add($"   Error: {error}");
                    }
                }
                
                if (model.ValidationResult != null && model.ValidationResult.Warnings.Count > 0)
                {
                    foreach (var warning in model.ValidationResult.Warnings)
                    {
                        summary.Add($"   Warning: {warning}");
                    }
                }
            }

            return string.Join(Environment.NewLine, summary);
        }

        public bool ValidateAllModels()
        {
            bool allValid = true;

            foreach (var (entityType, model) in _entityModels)
            {
                var validation = ValidateEntityModel(entityType, model);
                model.ValidationResult = validation;
                
                if (!validation.IsValid)
                {
                    allValid = false;
                    if (_validationMode == ValidationMode.Strict)
                    {
                        throw new InvalidOperationException($"Entity model validation failed for {entityType.Name}: {string.Join(", ", validation.Errors)}");
                    }
                }
            }

            return allValid;
        }

        private EntityModel CreateEntityModelFromType(Type entityType)
        {
            var topicAttribute = entityType.GetCustomAttribute<TopicAttribute>();
            var allProperties = entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var keyProperties = Array.FindAll(allProperties, p => p.GetCustomAttribute<KeyAttribute>() != null);

            // Key プロパティをOrder順にソート
            Array.Sort(keyProperties, (p1, p2) =>
            {
                var order1 = p1.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
                var order2 = p2.GetCustomAttribute<KeyAttribute>()?.Order ?? 0;
                return order1.CompareTo(order2);
            });

            var model = new EntityModel
            {
                EntityType = entityType,
                TopicAttribute = topicAttribute,
                AllProperties = allProperties,
                KeyProperties = keyProperties
            };

            // 検証実行
            model.ValidationResult = ValidateEntityModel(entityType, model);

            return model;
        }

        private ValidationResult ValidateEntityModel(Type entityType, EntityModel model)
        {
            var result = new ValidationResult { IsValid = true };

            // エンティティ型の基本検証
            if (!entityType.IsClass || entityType.IsAbstract)
            {
                result.IsValid = false;
                result.Errors.Add($"Entity type {entityType.Name} must be a concrete class");
            }

            // Topic属性の検証
            if (model.TopicAttribute == null)
            {
                if (_validationMode == ValidationMode.Strict)
                {
                    result.IsValid = false;
                    result.Errors.Add($"Entity {entityType.Name} must have [Topic] attribute");
                }
                else
                {
                    result.Warnings.Add($"Entity {entityType.Name} does not have [Topic] attribute, using class name as topic");
                }
            }

            // プロパティの検証
            foreach (var property in model.AllProperties)
            {
                if (!IsValidPropertyType(property.PropertyType))
                {
                    result.Warnings.Add($"Property {property.Name} has potentially unsupported type {property.PropertyType.Name}");
                }
            }

            // キープロパティの検証
            if (model.KeyProperties.Length == 0)
            {
                result.Warnings.Add($"Entity {entityType.Name} has no [Key] properties, will be treated as Stream");
            }

            return result;
        }

        private bool IsValidPropertyType(Type propertyType)
        {
            var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            return underlyingType.IsPrimitive ||
                   underlyingType == typeof(string) ||
                   underlyingType == typeof(decimal) ||
                   underlyingType == typeof(DateTime) ||
                   underlyingType == typeof(DateTimeOffset) ||
                   underlyingType == typeof(Guid) ||
                   underlyingType == typeof(byte[]);
        }
    }
}