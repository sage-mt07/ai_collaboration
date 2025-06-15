using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace KsqlDsl.Tests
{
    public class ForEachAsyncApiTests
    {
        private class DummyEntity { }

        [Fact]
        public void EventSet_ForEachAsync_Should_HaveTimeoutOverload()
        {
            var eventSetType = typeof(KsqlDsl.EventSet<>).MakeGenericType(typeof(DummyEntity));
            var methods = eventSetType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            bool hasTimeoutOverload = methods.Any(m =>
                m.Name == "ForEachAsync" &&
                m.GetParameters().Length == 3 &&
                m.GetParameters()[0].ParameterType == typeof(Func<DummyEntity, Task>) &&
                m.GetParameters()[1].ParameterType == typeof(TimeSpan) &&
                m.GetParameters()[2].ParameterType == typeof(CancellationToken));

            Assert.True(hasTimeoutOverload, "ForEachAsync with timeout and CancellationToken overload is missing.");
        }
    }
}
