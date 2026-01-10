# Couchbase.Aspire.Hosting library

Provides extension methods and resource definitions for an Aspire AppHost to configure a Couchbase cluster resource.

## Getting Started

### Install the package

```dotnetcli
dotnet add package Couchbase.Aspire.Hosting
```

## Usage example

In the _AppHost.cs_ file of your `AppHost`, add a Couchbase cluster resource and consume the connection using the following methods:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var servers = couchbase.AddServerGroup("couchbase_servers");
var bucket = servers.AddBucket("mybucket");

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(couchbase)
    .WaitFor(bucket);
```

## Connection properties

When you reference a Couchbase cluster resource using `WithReference`, the following connection properties are made available to the consuming project:

### Couchbase cluster

The Couchbase cluster resource exposes the following connection properties:

| Property Name | Description |
| ------------- | ----------- |
| `Username` | The username for authentication |
| `Password` | The password for authentication |
| `Uri` | The connection URI, with the format `couchbase://{Host}:{Port},{Host2}:{Port}` or `couchbases://{Host}:{Port},{Host2}:{Port}` |
| `BucketNameMap` | A comma-separated list of key/value pairs mapping resource names to bucket names, with the format `BucketResource1=Bucket1,BucketResource2=Bucket2` |

Aspire exposes each property as an environment variable named `[RESOURCE]_[PROPERTY]`. For instance, the `Uri` property of a resource called `cluster1` becomes `CLUSTER1_URI`.

### Couchbase bucket

Buckets do not produce any connection properties, they should be accessed via the cluster connection. However, the `BucketNameMap` does allow the consuming application to use either the resource name or the bucket name to access the bucket. Additionally, the bucket resource can be used to define dependencies for startup ordering using `WaitFor`.

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
