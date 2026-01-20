using Couchbase.Aspire.Client;
using Couchbase.Extensions.DependencyInjection;

namespace Aspire.Test.WebApp;

public interface ICounterCollectionProvider : INamedCollectionProvider;

public class CounterCollectionProvider(ICouchbaseClientProvider clientProvider)
    : NamedCollectionProvider(clientProvider, "test-scope", "counter"), ICounterCollectionProvider;
