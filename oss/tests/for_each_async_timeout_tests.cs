using System;
using System.Threading;
using System.Threading.Tasks;
using KsqlDsl;
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
    }

    public class TimeoutTestContext : KafkaContext
    {
        public EventSet<TimeoutTestEvent> Events => Set<TimeoutTestEvent>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Event<TimeoutTestEvent>();
        }

        protected override void OnConfiguring(KafkaContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseKafka("localhost:9092");
        }
    }

    public class ForEachAsyncTimeoutTests
    {
        [Fact]
        public async Task ForEachAsync_WithCancelledToken_Should_ThrowOperationCanceledException()
        {
            using var context = new TimeoutTestContext();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await context.Events.ForEachAsync(_ => Task.CompletedTask, cts.Token));
        }

        [Fact]
        public async Task ForEachAsync_WithTimeout_Should_Cancel()
        {
            using var context = new TimeoutTestContext();

            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await context.Events.ForEachAsync(_ => Task.Delay(50), TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public async Task ForEachAsync_WithSufficientTimeout_Should_Complete()
        {
            using var context = new TimeoutTestContext();
            int called = 0;

            await context.Events.ForEachAsync(_ => { called++; return Task.CompletedTask; }, TimeSpan.FromSeconds(1));

            Assert.Equal(0, called); // EventSet returns empty list in tests
        }
    }
}
