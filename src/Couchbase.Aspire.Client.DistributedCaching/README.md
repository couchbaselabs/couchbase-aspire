# Couchbase.Aspire.Client.DistributedCaching library

Registers an [IDistributedCache](https://learn.microsoft.com/dotnet/api/microsoft.extensions.caching.distributed.idistributedcache) in the DI container that connects to a Couchbase cluster. See [Distributed Caching](https://learn.microsoft.com/aspnet/core/performance/caching/distributed) for more information. Enables corresponding health check, logging, and telemetry.

## Getting Started

### Prerequisites

- Couchbase or Capella cluster and connection string for accessing the cluster.

### Install the package

```dotnetcli
dotnet add package Couchbase.Aspire.Client.DistributedCaching
```

## Usage example

In the _AppHost.cs_ file of your project, call the `AddCouchbaseDistributedCache` extension method to register an `IDistributedCache` for use via the dependency injection container. The method takes a connection name parameter.

```csharp
builder.AddCouchbaseDistributedCache("couchbase");
```

You can then retrieve the `IDistributedCache` or `ICouchbaseCache` instance using dependency injection. For example, to retrieve the cache from a Web API controller:

```csharp
private readonly IDistributedCache _cache;

public ProductsController(IDistributedCache cache)
{
    _cache = cache;
}
```

When using .NET 9.0 or later, you can register a `HybridCache` which caches both locally and in Couchbase. This also registers `IDistributedCache` and `ICouchbaseCache`.

```csharp
builder.AddCouchbaseHybridCache("couchbase");
```

## Configuration

The Aspire Couchbase component offers various options for configuring the database connection according to your project's requirements and conventions.

### Use a connection string

When using a connection string from the `ConnectionStrings` configuration section, you can provide the name of the connection string as the parameter when calling `AddCouchbaseDistributedCache`:

```csharp
builder.AddCouchbaseDistributedCache("myConnection");
```

And then the connection string will be retrieved from the `ConnectionStrings` configuration section:

```json
{
  "ConnectionStrings": {
    "myConnection": "couchbases://username:password@server1:11207,server2:11207/mybucket?option1=value1&option2=value2"
  }
}
```

See the [ConnectionString documentation](https://docs.couchbase.com/dotnet-sdk/current/howtos/managing-connections.html#connection-strings) for more information on how to format this connection string. `Couchbase.Aspire.Client` extends the standard connection string format by allowing the inclusion of the username, password, and bucket name directly in the connection string.

### Use configuration providers

The Aspire Couchbase Distributed Cache component supports [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration). It loads the `CouchbaseClientSettings` from configuration by using the `Aspire:Couchbase:Client` key. Example `appsettings.json` that configures some of the options:

```json
{
  "Aspire": {
    "Couchbase": {
      "Client": {
        "ConnectionString": "couchbases://server:port",
        "Username": "username",
        "Password": "password",
        "BucketName": "cache", // Optional bucket name
        "DisableHealthChecks": false,
        "DisableTracing": false,
        "HealthChecks": {
          "Type": "Active",
          "MinimumHealthyNodes": {
            "KeyValue": 2,
            "Query": 2
          },
          "MaximumUnhealthyNodes": {
            "KeyValue": 0,
            "Query": 1
          }
        }
      }
    }
  }
}
```

### Use inline delegates

You can also pass the `Action<CouchbaseClientSettings>` delegate to set up some or all of the options inline:

```csharp
    builder.AddCouchbaseDistributedCache("couchbase",
        settings => settings.ConnectionString = "couchbases://server:port");
```

Further tuning of `ClusterOptions` can be done by passing an additional `Action<ClusterOptions>` delegate:

```csharp
    builder.AddCouchbaseDistributedCache("couchbase",
        settings => settings.ConnectionString = "couchbases://server:port",
        options => options.WithSerializer(SystemTextJsonSerializer.Create());
```

## AppHost extensions

In your AppHost project, install the `Couchbase.Aspire.Hosting` library with  [NuGet](https://www.nuget.org):

```dotnetcli
dotnet add package Couchbase.Aspire.Hosting
```

Then, in the _AppHost.cs_ file of your project, register a Couchbase cluster and consume the connection using the following methods:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var cache = couchbase.AddBucket("cache")
    .WithBucketType(BucketType.Ephemeral);

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(cache)
    .WaitFor(cache);
```

The `WithReference` method configures a connection to a bucket in the `MyService` project named `cache`. In the _Program.cs_ file of `MyService`, the database connection can be consumed using:

```csharp
builder.AddCouchbaseDistributedCache("cache");
```

## Additional documentation

* https://learn.microsoft.com/aspnet/core/performance/caching/distributed
* https://github.com/dotnet/aspire/tree/main/src/Components/README.md

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
