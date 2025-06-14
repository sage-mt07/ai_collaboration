using System;
using System.Linq;
using System.Threading.Tasks;
using Ksql.EntityFrameworkCore;
using KsqlDsl.Attributes;
using KsqlDsl.Configuration;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using KsqlDsl.Validation;
using Xunit;

namespace KsqlDsl.Tests.SecondStage
{
    #region Test Entities

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

    [Topic("customer-events")]
    public class CustomerEvent
    {
        [Key]
        public string CustomerId { get; set; } = string.Empty;
        
        public string CustomerName { get; set; } = string.Empty;
        public DateTime UpdateTime { get; set; }
    }

    public class MissingAttributesEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    #endregion

    #region Sample KafkaContext Implementations

    /// <summary>
    /// 正常な設定を持つサンプルKafkaContext
    /// </summary>
    public class SampleKafkaContext : KafkaContext
    {
        public EventSet<TradeEvent> TradeEvents => Set<TradeEvent>();
        public EventSet<CustomerEvent> CustomerEvents => Set<CustomerEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<TradeEvent>();
            modelBuilder.Event<CustomerEvent>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .UseSchemaRegistry("http://localhost:8081")
                .UseConsumerGroup("sample-group")
                .EnableDebugLogging();
        }
    }

    /// <summary>
    /// ゆるめモードのサンプルKafkaContext
    /// </summary>
    public class RelaxedKafkaContext : KafkaContext
    {
        public EventSet<MissingAttributesEntity> MissingEntities => Set<MissingAttributesEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<MissingAttributesEntity>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .EnableRelaxedValidation(); // ゆるめモード
        }
    }

    /// <summary>
    /// 上書き設定を持つサンプルKafkaContext
    /// </summary>
    public class OverrideKafkaContext : KafkaContext
    {
        public EventSet<TradeEvent> TradeEvents => Set<TradeEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<TradeEvent>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .OverrideTopicOption<TradeEvent>(
                    partitionCount: 10, 
                    retentionMs: 3600000, 
                    reason: "Production scaling");
        }
    }

    /// <summary>
    /// OnConfiguring未実装（例外テスト用）
    /// </summary>
    public class InvalidKafkaContext : KafkaContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // 何も登録しない
        }

        // OnConfiguringをオーバーライドしない → UseKafka()未実行 → 例外
    }

    #endregion

    #region ModelBuilder Tests

    public class ModelBuilderTests
    {
        [Fact]
        public void ModelBuilder_Event_Should_RegisterEntitySuccessfully()
        {
            // Arrange
            var modelBuilder = new ModelBuilder(ValidationMode.Strict);

            // Act
            var entityBuilder = modelBuilder.Event<TradeEvent>();

            // Assert
            Assert.NotNull(entityBuilder);
            var entityModels = modelBuilder.GetEntityModels();
            Assert.Single(entityModels);
            Assert.True(entityModels.ContainsKey(typeof(TradeEvent)));
            
            var tradeEventModel = entityModels[typeof(TradeEvent)];
            Assert.Equal("trade-events", tradeEventModel.TopicAttribute?.TopicName);
            Assert.True(tradeEventModel.IsValid);
        }

        [Fact]
        public void ModelBuilder_Event_DuplicateRegistration_Should_ThrowException()
        {
            // Arrange
            var modelBuilder = new ModelBuilder(ValidationMode.Strict);
            modelBuilder.Event<TradeEvent>();

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => modelBuilder.Event<TradeEvent>());
            Assert.Contains("既に登録済み", exception.Message);
        }

        [Fact]
        public void ModelBuilder_StrictMode_MissingAttributes_Should_FailValidation()
        {
            // Arrange
            var modelBuilder = new ModelBuilder(ValidationMode.Strict);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => 
            {
                modelBuilder.Event<MissingAttributesEntity>();
                modelBuilder.Build();
            });
            Assert.Contains("モデル構築に失敗", exception.Message);
        }

        [Fact]
        public void ModelBuilder_RelaxedMode_MissingAttributes_Should_PassWithWarnings()
        {
            // Arrange
            var modelBuilder = new ModelBuilder(ValidationMode.Relaxed);

            // Act
            modelBuilder.Event<MissingAttributesEntity>();
            modelBuilder.Build(); // 例外をスローしない

            // Assert
            var entityModel = modelBuilder.GetEntityModel<MissingAttributesEntity>();
            Assert.NotNull(entityModel);
            Assert.True(entityModel.IsValid); // Relaxedモードでは有効
            Assert.NotEmpty(entityModel.ValidationResult?.Warnings ?? new());
        }

        [Fact]
        public void ModelBuilder_GetModelSummary_Should_ReturnExpectedSummary()
        {
            // Arrange
            var modelBuilder = new ModelBuilder(ValidationMode.Strict);
            modelBuilder.Event<TradeEvent>();
            modelBuilder.Event<CustomerEvent>();

            // Act
            var summary = modelBuilder.GetModelSummary();

            // Assert
            Assert.Contains("登録済みエンティティ: 2件", summary);
            Assert.Contains("TradeEvent", summary);
            Assert.Contains("CustomerEvent", summary);
            Assert.Contains("trade-events", summary);
            Assert.Contains("customer-events", summary);
        }
    }

    #endregion

    #region EventSet Tests

    public class EventSetTests
    {
        [Fact]
        public void EventSet_Creation_Should_InitializeCorrectly()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var tradeEvents = context.TradeEvents;

            // Assert
            Assert.NotNull(tradeEvents);
            Assert.Equal(typeof(TradeEvent), tradeEvents.ElementType);
            Assert.Equal("trade-events", tradeEvents.GetTopicName());
            Assert.Same(context, tradeEvents.GetContext());
        }

        [Fact]
        public async Task EventSet_AddAsync_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();
            var tradeEvent = new TradeEvent
            {
                TradeId = 123,
                Symbol = "USD/JPY",
                Amount = 1000000,
                TradeTime = DateTime.UtcNow,
                IsActive = true
            };

            // Act & Assert
            await context.TradeEvents.AddAsync(tradeEvent); // 例外をスローしない
        }

        [Fact]
        public async Task EventSet_AddRangeAsync_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();
            var tradeEvents = new[]
            {
                new TradeEvent { TradeId = 1, Symbol = "EUR/USD", Amount = 500000 },
                new TradeEvent { TradeId = 2, Symbol = "GBP/JPY", Amount = 750000 }
            };

            // Act & Assert
            await context.TradeEvents.AddRangeAsync(tradeEvents); // 例外をスローしない
        }

        [Fact]
        public void EventSet_ToList_Should_ReturnEmptyList()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = context.TradeEvents.ToList();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result); // モック実装では空リスト
        }

        [Fact]
        public async Task EventSet_ToListAsync_Should_ReturnEmptyList()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = await context.TradeEvents.ToListAsync();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result); // モック実装では空リスト
        }

        [Fact]
        public void EventSet_Subscribe_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();
            var receivedCount = 0;

            // Act & Assert
            context.TradeEvents.Subscribe(trade => receivedCount++); // 例外をスローしない
        }

        [Fact]
        public async Task EventSet_SubscribeAsync_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();
            var receivedCount = 0;

            // Act & Assert
            await context.TradeEvents.SubscribeAsync(async trade => 
            {
                receivedCount++;
                await Task.Delay(1);
            }); // 例外をスローしない
        }

        [Fact]
        public void EventSet_ToKsql_Should_ReturnValidKsqlString()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var ksql = context.TradeEvents.ToKsql();

            // Assert
            Assert.NotNull(ksql);
            Assert.Contains("SELECT", ksql);
            Assert.Contains("trade-events", ksql);
        }

        [Fact]
        public void EventSet_GetEntityModel_Should_ReturnCorrectModel()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var entityModel = context.TradeEvents.GetEntityModel();

            // Assert
            Assert.NotNull(entityModel);
            Assert.Equal(typeof(TradeEvent), entityModel.EntityType);
            Assert.Equal("trade-events", entityModel.TopicAttribute?.TopicName);
            Assert.True(entityModel.IsValid);
        }

        [Fact]
        public void EventSet_ToString_Should_ReturnExpectedString()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = context.TradeEvents.ToString();

            // Assert
            Assert.Contains("EventSet<TradeEvent>", result);
            Assert.Contains("trade-events", result);
        }
    }

    #endregion

    #region KafkaContext Tests

    public class KafkaContextTests
    {
        [Fact]
        public void KafkaContext_Creation_Should_InitializeCorrectly()
        {
            // Arrange & Act
            using var context = new SampleKafkaContext();

            // Assert
            Assert.NotNull(context.Options);
            Assert.Equal("localhost:9092", context.Options.ConnectionString);
            Assert.Equal("http://localhost:8081", context.Options.SchemaRegistryUrl);
            Assert.Equal("sample-group", context.Options.ConsumerGroupId);
            Assert.True(context.Options.EnableDebugLogging);
        }

        [Fact]
        public void KafkaContext_Set_Should_ReturnCorrectEventSet()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var tradeEvents1 = context.Set<TradeEvent>();
            var tradeEvents2 = context.Set<TradeEvent>();

            // Assert
            Assert.NotNull(tradeEvents1);
            Assert.Same(tradeEvents1, tradeEvents2); // 同じインスタンスを返すことを確認
            Assert.Equal("trade-events", tradeEvents1.GetTopicName());
        }

        [Fact]
        public void KafkaContext_Set_UnregisteredEntity_Should_ThrowException()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => context.Set<MissingAttributesEntity>());
            Assert.Contains("ModelBuilderに登録されていません", exception.Message);
        }

        [Fact]
        public void KafkaContext_PropertyAccess_Should_ReturnEventSet()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var tradeEvents = context.TradeEvents;
            var customerEvents = context.CustomerEvents;

            // Assert
            Assert.NotNull(tradeEvents);
            Assert.NotNull(customerEvents);
            Assert.Equal("trade-events", tradeEvents.GetTopicName());
            Assert.Equal("customer-events", customerEvents.GetTopicName());
        }

        [Fact]
        public void KafkaContext_GetEntityModels_Should_ReturnRegisteredModels()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var entityModels = context.GetEntityModels();

            // Assert
            Assert.Equal(2, entityModels.Count);
            Assert.True(entityModels.ContainsKey(typeof(TradeEvent)));
            Assert.True(entityModels.ContainsKey(typeof(CustomerEvent)));
        }

        [Fact]
        public async Task KafkaContext_EnsureCreatedAsync_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act & Assert
            await context.EnsureCreatedAsync(); // 例外をスローしない
        }

        [Fact]
        public void KafkaContext_EnsureCreated_Should_ExecuteWithoutException()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act & Assert
            context.EnsureCreated(); // 例外をスローしない
        }

        [Fact]
        public async Task KafkaContext_SaveChangesAsync_Should_Return0()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = await context.SaveChangesAsync();

            // Assert
            Assert.Equal(0, result); // Kafka流では常に0
        }

        [Fact]
        public void KafkaContext_SaveChanges_Should_Return0()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = context.SaveChanges();

            // Assert
            Assert.Equal(0, result); // Kafka流では常に0
        }

        [Fact]
        public void KafkaContext_GetDiagnostics_Should_ReturnDetailedInfo()
        {
            // Arrange
            using var context = new SampleKafkaContext();
            // ModelBuilderを構築するためにEventSetにアクセス
            _ = context.TradeEvents;

            // Act
            var diagnostics = context.GetDiagnostics();

            // Assert
            Assert.Contains("SampleKafkaContext", diagnostics);
            Assert.Contains("localhost:9092", diagnostics);
            Assert.Contains("sample-group", diagnostics);
            Assert.Contains("Model Built: True", diagnostics);
            Assert.Contains("TradeEvent", diagnostics);
            Assert.Contains("CustomerEvent", diagnostics);
        }

        [Fact]
        public void KafkaContext_ToString_Should_ReturnExpectedString()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act
            var result = context.ToString();

            // Assert
            Assert.Contains("SampleKafkaContext", result);
            Assert.Contains("localhost:9092", result);
        }

        [Fact]
        public void KafkaContext_InvalidConfiguration_Should_ThrowException()
        {
            // Arrange & Act & Assert
            Assert.Throws<InvalidOperationException>(() => new InvalidKafkaContext());
        }
    }

    #endregion

    #region Relaxed Mode Tests

    public class RelaxedModeTests
    {
        [Fact]
        public void RelaxedKafkaContext_Should_HandleMissingAttributes()
        {
            // Arrange & Act
            using var context = new RelaxedKafkaContext();

            // Assert - 例外をスローしないことを確認
            Assert.NotNull(context);
            Assert.Equal(ValidationMode.Relaxed, context.Options.ValidationMode);
        }

        [Fact]
        public void RelaxedKafkaContext_MissingEntities_Should_Work()
        {
            // Arrange
            using var context = new RelaxedKafkaContext();

            // Act
            var missingEntities = context.MissingEntities;

            // Assert
            Assert.NotNull(missingEntities);
            // ゆるめモードでは自動でクラス名をトピック名として使用
            Assert.Equal("MissingAttributesEntity", missingEntities.GetTopicName());
        }
    }

    #endregion

    #region Override Tests

    public class OverrideTests
    {
        [Fact]
        public void OverrideKafkaContext_Should_ApplyOverrides()
        {
            // Arrange
            using var context = new OverrideKafkaContext();

            // Act
            var overrides = context.Options.TopicOverrideService.GetAllOverrides();

            // Assert
            Assert.Single(overrides);
            Assert.True(overrides.ContainsKey(typeof(TradeEvent)));
            
            var tradeOverride = overrides[typeof(TradeEvent)];
            Assert.Equal(10, tradeOverride.PartitionCount);
            Assert.Equal(3600000, tradeOverride.RetentionMs);
            Assert.Equal("Production scaling", tradeOverride.Reason);
        }

        [Fact]
        public void OverrideKafkaContext_MergedConfig_Should_UseOverrideValues()
        {
            // Arrange
            using var context = new OverrideKafkaContext();

            // Act
            var mergedConfig = context.Options.TopicOverrideService.GetMergedTopicConfig(typeof(TradeEvent));

            // Assert
            Assert.Equal("trade-events", mergedConfig.TopicName); // 属性値
            Assert.Equal(10, mergedConfig.PartitionCount); // 上書き値
            Assert.Equal(2, mergedConfig.ReplicationFactor); // 属性値
            Assert.Equal(3600000, mergedConfig.RetentionMs); // 上書き値
            Assert.True(mergedConfig.HasOverride);
            Assert.Equal("Production scaling", mergedConfig.OverrideReason);
        }
    }

    #endregion

    #region Dispose Tests

    public class DisposeTests
    {
        [Fact]
        public void KafkaContext_Dispose_Should_ExecuteWithoutException()
        {
            // Arrange
            var context = new SampleKafkaContext();
            _ = context.TradeEvents; // EventSetを初期化

            // Act & Assert
            context.Dispose(); // 例外をスローしない
        }

        [Fact]
        public async Task KafkaContext_DisposeAsync_Should_ExecuteWithoutException()
        {
            // Arrange
            var context = new SampleKafkaContext();
            _ = context.TradeEvents; // EventSetを初期化

            // Act & Assert
            await context.DisposeAsync(); // 例外をスローしない
        }

        [Fact]
        public void KafkaContext_UsingStatement_Should_DisposeCorrectly()
        {
            // Arrange & Act & Assert
            using (var context = new SampleKafkaContext())
            {
                _ = context.TradeEvents;
                // usingブロック終了時に自動でDisposeされる
            } // 例外をスローしない
        }
    }

    #endregion

    #region Integration Tests

    public class IntegrationTests
    {
        [Fact]
        public async Task CompleteWorkflow_Should_ExecuteSuccessfully()
        {
            // Arrange
            using var context = new SampleKafkaContext();

            // Act - 完全なワークフローテスト
            await context.EnsureCreatedAsync();

            var tradeEvent = new TradeEvent
            {
                TradeId = 999,
                Symbol = "BTC/USD",
                Amount = 50000,
                TradeTime = DateTime.UtcNow,
                IsActive = true
            };

            await context.TradeEvents.AddAsync(tradeEvent);
            var trades = await context.TradeEvents.ToListAsync();
            var ksql = context.TradeEvents.ToKsql();

            // Assert
            Assert.NotNull(trades);
            Assert.NotNull(ksql);
            Assert.Contains("trade-events", ksql);
        }

        [Fact]
        public void MultipleContexts_Should_WorkIndependently()
        {
            // Arrange & Act
            using var context1 = new SampleKafkaContext();
            using var context2 = new RelaxedKafkaContext();

            // Assert
            Assert.Equal(ValidationMode.Strict, context1.Options.ValidationMode);
            Assert.Equal(ValidationMode.Relaxed, context2.Options.ValidationMode);
            
            Assert.NotSame(context1.TradeEvents, context2.MissingEntities);
        }
    }

    #endregion
}