namespace Couchbase.Aspire.Hosting;

[Flags]
public enum CouchbaseServices
{
    Default = 0,
    Data = 1,
    Query = 2,
    Index = 4,
    Fts = 8,
    Analytics = 16,
    Eventing = 32,
    Backup = 64,
}
