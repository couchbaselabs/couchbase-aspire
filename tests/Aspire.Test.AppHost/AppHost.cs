using Couchbase.Aspire.Hosting;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;

var builder = DistributedApplication.CreateBuilder(args);

var couchbase = builder.AddCouchbase("couchbase")
    .WithManagementPort(8091) // Optional fixed port number for the primary node
    .WithSecureManagementPort(18091) // Optional fixed port number for the primary node
    .WithCouchbaseEdition(CouchbaseEdition.Enterprise); // Optional edition, default is Enterprise

// Uncomment this section to test building a secure cluster. Note that the test web app will not be
// able to connect unless the certificate is trusted on the host machine.
// var certificate = Aspire.Test.AppHost.Helpers.LoadCACertificate("CouchbaseCA.pfx");
// couchbase.WithRootCertificationAuthority(certificate, trustCertificate: true);

var couchbaseGroup1 = couchbase.AddServerGroup("couchbase-group1", CouchbaseServices.Data | CouchbaseServices.Query | CouchbaseServices.Index | CouchbaseServices.Fts)
    .WithReplicas(2);

var couchbaseGroup2 = couchbase.AddServerGroup("couchbase-group2", CouchbaseServices.Analytics | CouchbaseServices.Eventing)
    .WithReplicas(2);

var testBucket = couchbase.AddBucket("test-bucket", bucketName: "test")
    .WithMemoryQuota(200) // Optional memory quota, default is 100MB
    .WithConflictResolutionType(ConflictResolutionType.Timestamp)
    .WithMinimumDurabilityLevel(DurabilityLevel.MajorityAndPersistToActive)
    .WithEvictionPolicy(EvictionPolicyType.FullEviction)
    .WithCompressionMode(CompressionMode.Active)
    .WithReplicas(0);

var cacheBucket = couchbase.AddBucket("cache-bucket", bucketName: "cache")
    .WithBucketType(BucketType.Ephemeral)
    .WithReplicas(0)
    .WithEvictionPolicy(EvictionPolicyType.NotRecentlyUsed)
    .WithFlushEnabled()
    .WithMaximumTimeToLive(300);

builder.AddProject<Projects.Aspire_Test_WebApp>("aspire-test-webapp")
    .WithReference(couchbase)
    .WaitFor(testBucket);

builder.Build().Run();
