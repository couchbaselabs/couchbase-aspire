# Couchbase.Aspire.Client library

Registers [IClusterProvider](https://docs.couchbase.com/dotnet-sdk/current/howtos/dependency-injection.html#clusterprovider) and [IBucketProvider](https://docs.couchbase.com/dotnet-sdk/current/howtos/dependency-injection.html#bucketprovider) in the DI container for connecting Couchbase databases.

## Getting Started

### Prerequisites

- Couchbase or Capella cluster and connection string for accessing the cluster.

### Install the package

```dotnetcli
dotnet add package Couchbase.Aspire.Client
```

## Usage example

In the _AppHost.cs_ file of your project, call the `AddCouchbaseClient` extension method to register `IClusterProvider` and `IBucketProvider` for use via the dependency injection container. The method takes a connection name parameter.

```csharp
builder.AddCouchbaseClient("couchbase");
```

You can then retrieve the `IClusterProvider` and `IBucketProvider` instances using dependency injection. For example, to retrieve a connection from a Web API controller:

```csharp
private readonly IBucketProvider _bucketProvider;

public ProductsController(IBucketProvider bucketProvider)
{
    _bucketProvider = bucketProvider;
}
```

## Configuration

The Aspire Couchbase component offers various options for configuring the database connection according to your project's requirements and conventions.

### Use environment variables

### Use configuration providers

The Aspire Couchbase component supports [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration). It loads the `CouchbaseClientSettings` from configuration by using the `Aspire:Couchbase:Client` key. Example `appsettings.json` that configures some of the options:

```json
{
  "Aspire": {
    "Couchbase": {
      "Client": {
        "ConnectionString": "couchbases://server:port",
        "Username": "username",
        "Password": "password",
        "DisableHealthChecks": false,
        "DisableTracing": false
      }
    }
  }
}
```

If the configuration provider uses `AddEnvironmentVariables()`, you can also set the options using environment variables. Note that the prefix "COUCHBASE_" in the example below is based on the `"couchbase"` connection string name passed to `AddCouchbaseClient`.

```sh
export COUCHBASE_URI="couchbases://server:port"
export COUCHBASE_USERNAME="username"
export COUCHBASE_PASSWORD="password"
```

### Use inline delegates

You can also pass the `Action<CouchbaseClientSettings>` delegate to set up some or all of the options inline:

```csharp
    builder.AddCouchbaseClient("couchbase",
        settings => settings.ConnectionString = "couchbases://server:port");
```

Further tuning of `ClusterOptions` can be done by passing an additional `Action<ClusterOptions>` delegate:

```csharp
    builder.AddCouchbaseClient("couchbase",
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
var servers = couchbase.AddServerGroup("couchbase_servers");
var bucket = servers.AddBucket("mybucket");

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(couchbase)
    .WaitFor(bucket);
```

The `WithReference` method configures a connection in the `MyService` project named `couchbase`. In the _Program.cs_ file of `MyService`, the database connection can be consumed using:

```csharp
builder.AddCouchbaseClient("couchbase");
```

## Additional documentation

* https://docs.couchbase.com/dotnet-sdk/current/hello-world/start-using-sdk.html
* https://github.com/dotnet/aspire/tree/main/src/Components/README.md

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
