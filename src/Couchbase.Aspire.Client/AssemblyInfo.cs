using Aspire;
using Couchbase;
using Couchbase.Aspire.Client;

[assembly: ConfigurationSchema("Aspire:Couchbase:Client", typeof(CouchbaseClientSettings))]
[assembly: ConfigurationSchema("Aspire:Couchbase:Client:ClusterOptions", typeof(ClusterOptions))]

[assembly: LoggingCategories("Couchbase")]
