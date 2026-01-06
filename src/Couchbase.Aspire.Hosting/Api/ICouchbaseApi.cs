
namespace Couchbase.Aspire.Hosting.Api;

internal interface ICouchbaseApi
{
    Task AddNodeAsync(CouchbaseServerResource server, string hostname, CouchbaseServices services, CancellationToken cancellationToken = default);
    Task CreateBucketAsync(CouchbaseServerResource server, string bucketName, CouchbaseBucketSettings settings, CancellationToken cancellationToken = default);
    Task<SampleBucketResponse> CreateSampleBucketAsync(CouchbaseServerResource server, string bucketName, CancellationToken cancellationToken);
    Task<Bucket?> GetBucketAsync(CouchbaseServerResource server, string bucketName, CancellationToken cancellationToken);
    Task<Pool> GetClusterNodesAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default);
    Task<bool> GetDefaultPoolAsync(CouchbaseServerResource server, bool preferInsecure = false, CancellationToken cancellationToken = default);
    Task<RebalanceStatus> GetRebalanceProgressAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default);
    Task InitializeClusterAsync(CouchbaseServerResource server, CouchbaseClusterSettings settings, CancellationToken cancellationToken = default);
    Task LoadTrustedCAsAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default);
    Task RebalanceAsync(CouchbaseServerResource server, List<string> knownNodes, CancellationToken cancellationToken = default);
    Task ReloadCertificateAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default);
    Task SetupAlternateAddressesAsync(CouchbaseServerResource server, string hostname, Dictionary<string, string> ports, CancellationToken cancellationToken = default);
    Task<List<ClusterTask>> GetClusterTasksAsync(CouchbaseServerResource server, CancellationToken cancellationToken = default);
}
