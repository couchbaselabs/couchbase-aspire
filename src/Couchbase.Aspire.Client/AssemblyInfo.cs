using Aspire;
using Couchbase;
using Couchbase.Aspire.Client;
using System.Runtime.CompilerServices;

[assembly: ConfigurationSchema("Aspire:Couchbase:Client", typeof(CouchbaseClientSettings))]
[assembly: ConfigurationSchema("Aspire:Couchbase:Client:ClusterOptions", typeof(ClusterOptions))]

[assembly: LoggingCategories("Couchbase")]

[assembly: InternalsVisibleTo("Couchbase.Aspire.Client.UnitTests, PublicKey=00240000048000009400000006020000002400005253413100040000010001006D2EC6C31E4387EC092962930CDE9A0A83A85DBC77E2F8CA7F00369DAE2F0B92A334075920343E8E855FCF604F92A1F97A2282CD44C103034560A8B6BF4939D7B8AFBC40E557222114C5396BA6EF1107E0A122B64E795CAEFE59095C446206EECBDF81D23C0C286D7F0038BDA3915B16EB3CF10EEA5E1E76E6A46E94F69FF6D8")]
