# Couchbase.Aspire.Hosting library

Provides extension methods and resource definitions for an Aspire AppHost to configure a Couchbase cluster resource.

## Getting Started

### Prerequisites

- Aspire 13.6.5 or later. The AppHost must use `Aspire.AppHost.Sdk` 13.6.5 or later, and any direct `Aspire.Hosting` reference must be 13.6.5 or later.

### Install the package

```dotnetcli
dotnet add package Couchbase.Aspire.Hosting
```

## Usage example

In the _AppHost.cs_ file of your `AppHost`, add a Couchbase cluster resource with the data, query, and index services
and consume the connection using the following methods:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var bucket = couchbase.AddBucket("mybucket");

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(bucket)
    .WaitFor(bucket);
```

### Multi-dimensional scaling example

It is also possible to run a multiple server cluster with various services assigned to different server groups:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var bucket1 = couchbase.AddBucket("bucket1");

var group1 = couchbase.AddServerGroup("couchbase-group1")
    .WithServices(CouchbaseServices.Data)
    .WithReplicas(2);

var group2 = couchbase.AddServerGroup("couchbase-group2")
    .WithServices(CouchbaseServices.Index | CouchbaseServices.Query)
    .WithReplicas(2);

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(bucket)
    .WaitFor(bucket);
```

## MCP server

Add the [Couchbase MCP server](https://hub.docker.com/r/couchbase/mcp-server) to a cluster with `WithCouchbaseMcpServer`. The container is named `{cluster}-mcp`, receives its connection information from the cluster, is hidden in the Aspire dashboard, and is registered with Aspire's `WithMcpServer` so it is proxied through the Aspire MCP server.

This method is experimental (diagnostic `ASPIREMCP001`), matching Aspire's `WithMcpServer`.

```csharp
#pragma warning disable ASPIREMCP001

var couchbase = builder.AddCouchbase("couchbase")
    .WithCouchbaseMcpServer(mcp => mcp
        .WithReadOnlyMode() // Optional, default is read/write
        .WithImageTag("latest")); // Optional, run a different couchbase/mcp-server tag than the pinned default
```

The host port is dynamic; use the Aspire MCP server to reach it.

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
