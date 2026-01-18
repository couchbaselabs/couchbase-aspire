# Couchbase.Aspire.Hosting.Indices library

Provides extension methods and resource definitions to add index management to a Couchbase cluster resource in an Aspire AppHost.

## Getting Started

### Install the package

```dotnetcli
dotnet add package Couchbase.Aspire.Hosting.Indices
```

## Usage example

In the _AppHost.cs_ file of your `AppHost`, add indices to a Couchbase bucket resource using the following methods:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var bucket = couchbase.AddBucket("mybucket")
    .WithIndices("../indices");

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(bucket)
    .WaitFor(bucket);
```

### Waiting for indices to be ready

It is also possible to wait for the indices to be applied before starting your application:

```csharp
var couchbase = builder.AddCouchbase("couchbase");
var bucket = couchbase.AddBucket("mybucket");

var bucketIndices = bucket.AddIndexManager("mybucket-indices")
    .WithIndices("../indices");

var myService = builder.AddProject<Projects.MyService>()
    .WithReference(bucket)
    .WaitFor(bucket)
    .WaitForCompletion(bucketIndices);
```

## Index definition files

Index definition files are JSON or YAML files that define the indexes to be created on a Couchbase bucket. They may be referenced by directory
or by individual file. See [couchbase-index-manager Documentation](https://github.com/brantburnett/couchbase-index-manager/tree/main/packages/couchbase-index-manager-cli#definition-files) for details on the file format.

## Feedback & contributing

https://github.com/couchbaselabs/couchbase-aspire
