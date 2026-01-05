using System.Collections.Concurrent;

namespace Couchbase.Aspire.Hosting.Orchestration;

internal record OnCouchbaseResourceStartingEvent(ICouchbaseCustomResource Resource);
internal record OnCouchbaseResourceStartedEvent(ICouchbaseCustomResource Resource);
internal record OnCouchbaseResourceStoppingEvent(ICouchbaseCustomResource Resource);
internal record OnCouchbaseResourceStoppedEvent(ICouchbaseCustomResource Resource);

internal sealed class CouchbaseOrchestratorEvents
{
    private readonly ConcurrentDictionary<Type, Func<object, CancellationToken, Task>> _eventSubscriptionListLookup = new();

    public void Subscribe<T>(Func<T, CancellationToken, Task> callback) where T : notnull
    {
        var success = _eventSubscriptionListLookup.TryAdd(typeof(T), (obj, ct) => callback((T)obj, ct));
        if (!success)
        {
            throw new InvalidOperationException($"Failed to add subscription for event type {typeof(T)} because a subscription already exists.");
        }
    }

    public async Task PublishAsync<T>(T context, CancellationToken cancellationToken) where T : notnull
    {
        if (_eventSubscriptionListLookup.TryGetValue(typeof(T), out var callback))
        {
            await callback(context, cancellationToken).ConfigureAwait(false);
        }
    }
}
