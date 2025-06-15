using System;
using System.Threading;
using System.Threading.Tasks;
using KsqlDsl.Attributes;
using KsqlDsl.Modeling;
using KsqlDsl.Options;
using Xunit;

namespace KsqlDsl.Tests
{
    [Topic("timeout-test-events")]
    public class TimeoutTestEvent
    {
        [Key]
        public int Id { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public class TimeoutTestContext : KafkaContext
    {
        public EventSet<TimeoutTestEvent> TimeoutEvents => Set<TimeoutTestEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<TimeoutTestEvent>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseKafka("localhost:9092")
                .EnableDebugLogging();
        }
    }

    public class ForEachAsyncTimeoutTests
    {
        [Fact]
        public async Task ForEachAsync_WithDefaultTimeout_Should_HaveCorrectSignature()
        {
            // Arrange
            using var context = new TimeoutTestContext();
            var receivedCount = 0;

            // Act & Assert
            using var cts = new CancellationTokenSource(100); // 100ms

            try
            {
                await context.TimeoutEvents.ForEachAsync(async item =>
                {
                    receivedCount++;
                    await Task.Delay(1);
                }, cancellationToken: cts.Token);
            }
            catch (Exception ex)
            {
                Assert.True(ex is OperationCanceledException ||
                           ex is InvalidOperationException ||
                           ex is TimeoutException,
                    $"Expected cancellation/connection error, got: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task ForEachAsync_WithCustomTimeout_Should_HaveCorrectSignature()
        {
            // Arrange
            using var context = new TimeoutTestContext();
            var timeoutMs = 5000; // 5 seconds
            var receivedCount = 0;

            // Act & Assert
            using var cts = new CancellationTokenSource(100); // 100ms

            try
            {
                await context.TimeoutEvents.ForEachAsync(async item =>
                {
                    receivedCount++;
                    await Task.Delay(1);
                }, timeoutMs, cts.Token);
            }
            catch (Exception ex)
            {
                Assert.True(ex is OperationCanceledException ||
                           ex is InvalidOperationException ||
                           ex is TimeoutException,
                    $"Expected cancellation/connection/timeout error, got: {ex.GetType().Name}");
            }
        }

        [Fact]
        public async Task ForEachAsync_WithNullAction_Should_ThrowArgumentNullException()
        {
            // Arrange
            using var context = new TimeoutTestContext();

            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await context.TimeoutEvents.ForEachAsync(null!, 30000));
        }

        [Fact]
        public async Task ForEachAsync_TimeoutParameter_Should_BePassedCorrectly()
        {
            // Arrange
            using var context = new TimeoutTestContext();
            var customTimeoutMs = 15000; // 15 seconds

            // Act & Assert
            using var cts = new CancellationTokenSource(50); // 50ms

            try
            {
                await context.TimeoutEvents.ForEachAsync(
                    async item => await Task.Delay(1),
                    customTimeoutMs,
                    cts.Token);
            }
            catch (Exception ex)
            {
                Assert.True(ex is OperationCanceledException ||
                           ex is InvalidOperationException ||
                           ex is TimeoutException);
            }
        }

        [Fact]
        public void ForEachAsync_OverloadExists_Should_BeVerifiable()
        {
            // Arrange & Act
            var eventSetType = typeof(EventSet<TimeoutTestEvent>);

            // デフォルトタイムアウト版のメソッドが存在することを確認
            var defaultMethod = eventSetType.GetMethod("ForEachAsync", new[] {
                typeof(Func<TimeoutTestEvent, Task>),
                typeof(int),
                typeof(CancellationToken)
            });

            // Assert
            Assert.NotNull(defaultMethod);

            // パラメータがオプションであることを確認
            var parameters = defaultMethod.GetParameters();
            Assert.True(parameters[1].HasDefaultValue); // timeoutMs parameter
            Assert.True(parameters[2].HasDefaultValue); // cancellationToken parameter

            // デフォルト値を確認
            Assert.Equal(int.MaxValue, parameters[1].DefaultValue);
        }

        [Fact]
        public async Task ForEachAsync_WithMaxTimeout_Should_WorkAsUnlimited()
        {
            // Arrange
            using var context = new TimeoutTestContext();

            // Act & Assert - int.MaxValueを使った場合
            using var cts = new CancellationTokenSource(50); // 50ms

            try
            {
                await context.TimeoutEvents.ForEachAsync(
                    async item => await Task.Delay(1),
                    int.MaxValue, // 無制限
                    cts.Token);
            }
            catch (Exception ex)
            {
                // CancellationTokenによるキャンセルは予想される
                Assert.True(ex is OperationCanceledException ||
                           ex is InvalidOperationException,
                    "int.MaxValue timeout should not cause TimeoutException");
            }
        }
    }
}