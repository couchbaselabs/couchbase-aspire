using Couchbase.Aspire.Hosting;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var couchbasePassword = builder.AddParameter("couchbase-password", "password", secret: true);

var couchbase = builder.AddCouchbase("couchbase", password: couchbasePassword)
    .WithManagementPort(8091) // Optional fixed port number for the primary node
    .WithCouchbaseEdition(CouchbaseEdition.Enterprise); // Optional edition, default is Enterprise

var couchbaseGroup1 = couchbase.AddServerGroup("couchbase-group1")
    .WithServices(CouchbaseServices.Data | CouchbaseServices.Query | CouchbaseServices.Index | CouchbaseServices.Search)
    .WithReplicas(2);

var couchbaseGroup2 = couchbase.AddServerGroup("couchbase-group2")
    .WithServices(CouchbaseServices.Analytics | CouchbaseServices.Eventing)
    .WithReplicas(2);

var testBucket = couchbase.AddBucket("test-bucket", bucketName: "test")
    .WithMemoryQuota(200) // Optional memory quota, default is 100MB
    .WithConflictResolutionType(ConflictResolutionType.Timestamp)
    .WithMinimumDurabilityLevel(DurabilityLevel.MajorityAndPersistToActive)
    .WithEvictionPolicy(EvictionPolicyType.FullEviction)
    .WithCompressionMode(CompressionMode.Active)
    .WithReplicas(0);

var testIndices = testBucket.AddIndexManager("test-indices")
    .WithIndices("test-indices");

var cacheBucket = couchbase.AddBucket("cache-bucket", bucketName: "cache")
    .WithBucketType(BucketType.Ephemeral)
    .WithReplicas(0)
    .WithEvictionPolicy(EvictionPolicyType.NotRecentlyUsed)
    .WithFlushEnabled()
    .WithMaximumTimeToLive(300);

var sampleBucket = couchbase.AddSampleBucket("travel-sample-bucket", "travel-sample");

builder.AddProject<Projects.Aspire_Test_WebApp>("aspire-test-webapp")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(testBucket).WaitFor(testBucket)
    .WaitForCompletion(testIndices);

if (builder.Configuration.GetValue("COUCHBASE_SECURE", false))
{
    // Note that the test web app will not be able to connect unless the certificate is trusted on the host machine.
    var certAuthorityCollection = builder.AddCertificateAuthorityCollection("couchbase-cert-authority")
        .WithCertificate(Aspire.Test.AppHost.Helpers.LoadCACertificate("CouchbaseCA.pfx"));

    couchbase
        .WithSecureManagementPort(18091) // Optional fixed port number for the primary node
        .WithRootCertificationAuthority(certAuthorityCollection, trustCertificate: true);

    // Trust the Couchbase CA for the index manager
    testIndices.WithCertificateAuthorityCollection(certAuthorityCollection);
}

builder.Build().Run();
