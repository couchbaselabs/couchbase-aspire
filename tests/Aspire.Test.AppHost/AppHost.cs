using Couchbase.Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var couchbase = builder.AddCouchbase("couchbase")
    .WithManagementPort(8091); // Optional fixed port number for the primary node

var couchbaseGroup1 = couchbase.AddServerGroup("couchbase-group1", CouchbaseServices.Data | CouchbaseServices.Query | CouchbaseServices.Index | CouchbaseServices.Fts)
    .WithReplicas(2);

var couchbaseGroup2 = couchbase.AddServerGroup("couchbase-group2", CouchbaseServices.Analytics | CouchbaseServices.Eventing)
    .WithReplicas(2);

var testBucket = couchbase.AddBucket("test-bucket", bucketName: "test")
    .WithMemoryQuota(200); // Optional memory quota, default is 100MB

builder.AddProject<Projects.Aspire_Test_WebApp>("aspire-test-webapp")
    .WithReference(couchbase).WaitFor(testBucket);

builder.Build().Run();
