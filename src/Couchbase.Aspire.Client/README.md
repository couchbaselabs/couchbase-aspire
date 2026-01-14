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

If the connection string includes a bucket name, you can get the bucket instance via `INamedBucketProvider` without specifying the bucket name again:

```csharp
private readonly INamedBucketProvider _bucketProvider;

public ProductsController(INamedBucketProvider bucketProvider)
{
    _bucketProvider = bucketProvider;
}
```

## Configuration

The Aspire Couchbase component offers various options for configuring the database connection according to your project's requirements and conventions.

### Use a connection string

When using a connection string from the `ConnectionStrings` configuration section, you can provide the name of the connection string as the parameter when calling `AddCouchbaseClient`:

```csharp
builder.AddCouchbaseClient("myConnection");
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

The Aspire Couchbase component supports [Microsoft.Extensions.Configuration](https://learn.microsoft.com/dotnet/api/microsoft.extensions.configuration). It loads the `CouchbaseClientSettings` from configuration by using the `Aspire:Couchbase:Client` key. Example `appsettings.json` that configures some of the options:

```json
{
  "Aspire": {
    "Couchbase": {
      "Client": {
        "ConnectionString": "couchbases://server:port",
        "Username": "username",
        "Password": "password",
        "BucketName": "mybucket", // Optional bucket name
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
    .WithReference(bucket)
    .WaitFor(bucket);

// Altertively, reference the cluster rather than a specific bucket, INamedBucketProvider will not be registered in DI
var myService2 = builder.AddProject<Projects.MyService>()
    .WithReference(couchbase)
    .WaitFor(couchbase);
```

The `WithReference` method configures a connection to a bucket in the `MyService` project named `mybucket`. In the _Program.cs_ file of `MyService`, the database connection can be consumed using:

```csharp
builder.AddCouchbaseClient("mybucket");
```

## Health Checks

By default, health checks are enabled with the following configuration:

- Active, not passive
- Only check the Key/Value data service
- Require at least one healthy node, and allow no unhealthy nodes-

These defaults can be overridden using configuration providers or inline delegates as described in the Configuration section above.

## Additional documentation

* https://docs.couchbase.com/dotnet-sdk/current/hello-world/start-using-sdk.html
* https://github.com/dotnet/aspire/tree/main/src/Components/README.md

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
