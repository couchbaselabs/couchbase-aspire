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
    .WithReference(bucket)
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

The `ConnectionString` property is also available, which exposes all properties as a single string in the format `couchbase://{Username}:{Password}@{Host}:{Port},{Host2}:{Port}`.


### Couchbase bucket

The Couchbase bucket resource exposes the following connection properties:

| Property Name | Description |
| ------------- | ----------- |
| `Username` | The username for authentication |
| `Password` | The password for authentication |
| `Uri` | The connection URI, with the format `couchbase://{Host}:{Port},{Host2}:{Port}` or `couchbases://{Host}:{Port},{Host2}:{Port}` |
| `BucketName` | The name of the bucket |

The `ConnectionString` property is also available, which exposes all properties as a single string in the format `couchbase://{Username}:{Password}@{Host}:{Port},{Host2}:{Port}/{BucketName}`.

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
