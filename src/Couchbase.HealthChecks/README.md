# Couchbase Health Check

This health check verifies the ability to communicate with [Couchbase](https://www.couchbase.com/). It uses the
provided `ICluster` and monitors diagnostics or actively pings services or buckets.

## Defaults

By default, the `ICluster` is resolved from `IClusterProvider` from the service provider.

```csharp
void Configure(IHealthChecksBuilder healthChecksBuilder)
{
    healthChecksBuilder
        .AddCouchbase(options => {
            options.ConnectionString = "couchbase://localhost";
            options.Username = "username";
            options.Password = "password";
        })
        .AddHealthChecks()
        .AddCouchbase(); // Adds the health check using IClusterProvider from DI
}
```

By default, the health check only validates connectivity to the key/value service. It requires at least one healthy
connection to each data node in the cluster.

## Customization

You can additionally add the following parameters:

- `clusterFactory`: A delegate that receives an `IServiceProvider` and returns an `ICluster` instance to use for the health check.
- `serviceRequirmentsFactory`: A delegate that receives an `IServiceProvider` and returns a dictionary of requirements for each
  service to be monitored that define if the service is healthy or unhealthy.
- `bucketNameFactory`: A delegate that receives an `IServiceProvider` and returns the name of the bucket to monitor.
- `healthCheckType`: An enum value that defines the type of health check to perform. The default is `HealthCheckType.Active`, which
  actively pings services on all nodes for each health check. For a lower overhead option, you can use `HealthCheckType.Passive`, which simply monitors the state of the existing service connections.
- `name`: The health check name. The default is "couchbase".
- `failureStatus`: The `HealthStatus` that should be reported when the health check fails. The default is `HealthStatus.Unhealthy`.
- `tags`: A list of tags that can be used to filter sets of health checks.
- `timeout`: A `System.TimeSpan` representing the timeout of the check.

```csharp
void Configure(IHealthChecksBuilder healthChecksBuilder)
{
    healthChecksBuilder
        .AddCouchbase(options => {
            options.ConnectionString = "couchbase://localhost";
            options.Username = "username";
            options.Password = "password";
        })
        .AddHealthChecks()
        .AddCouchbase(
            serviceRequirementsFactory: sp =>
            {
                var requirements = CouchbaseHealthCheck.CreateDefaultServiceRequirements();
                requirements[ServiceType.Query] = [CouchbaseServiceHealthNodeRequirement.OneHealthyNode];
                return requirements;
            },
            bucketNameFactory: sp => "my-bucket");
}
```

Health checks may be further customized by creating custom `ICouchbaseServiceHealthRequirement` implementations. It is also possible
to inherit from `CouchbaseActiveHealthCheck` or `CouchbasePassiveHealthCheck` to create entirely custom health check implementations.
