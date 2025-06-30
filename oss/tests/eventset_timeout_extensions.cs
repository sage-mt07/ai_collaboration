using System;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl
{
    /// <summary>
    /// Extension methods for EventSet to support timeout parameter in tests.
    /// </summary>
    public static class EventSetTimeoutExtensions
    {
        public static async Task ForEachAsync<T>(this EventSet<T> set, Func<T, Task> action, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            await set.ForEachAsync(action, cts.Token);
        }
    }
}
