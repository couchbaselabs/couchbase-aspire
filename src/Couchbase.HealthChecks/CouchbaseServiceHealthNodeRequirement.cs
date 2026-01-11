using System.Diagnostics;
using System.Text;
using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Requires a minimum number of healthy nodes and a maximum number of unhealthy nodes for a service to report healthy.
/// </summary>
[DebuggerDisplay("MinimumHealthyNodes = {MinimumHealthyNodes,nq}, MaximumUnhealthyNodes = {MaximumUnhealthyNodes,nq}")]
public class CouchbaseServiceHealthNodeRequirement : ICouchbaseServiceHealthRequirement
{
    /// <summary>
    /// Minimum number of healthy nodes required to report healthy.
    /// </summary>
    /// <value>
    /// Defaults to <c>1</c>.
    /// </value>
    public int MinimumHealthyNodes { get; set; } = 1;

    /// <summary>
    /// Maximum number of unhealthy nodes allowed to report healthy.
    /// </summary>
    /// <value>
    /// Defaults to <see cref="int.MaxValue"/>.
    /// </value>
    public int MaximumUnhealthyNodes { get; set; } = int.MaxValue;

    /// <inheritdoc />
    public virtual ValueTask<HealthCheckResult> ValidateAsync(HealthCheckContext context, ServiceType serviceType,
        IEnumerable<IEndpointDiagnostics> endpoints, CancellationToken cancellationToken = default)
    {
        var allNodes = new HashSet<string>();
        var healthyNodes = new HashSet<string>();

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Remote is not null)
            {
                allNodes.Add(endpoint.Remote);

                if (IsEndpointHealthy(endpoint))
                {
                    healthyNodes.Add(endpoint.Remote);
                }
            }
        }

        if (healthyNodes.Count < MinimumHealthyNodes ||
            (allNodes.Count - healthyNodes.Count) > MaximumUnhealthyNodes)
        {
            return new ValueTask<HealthCheckResult>(new HealthCheckResult(context.Registration.FailureStatus,
                BuildFailedResultMessage(serviceType, allNodes, healthyNodes)));
        }

        return new ValueTask<HealthCheckResult>(HealthCheckResult.Healthy());
    }

    /// <summary>
    /// Returns <c>true</c> if the endpoint is considered healthy.
    /// </summary>
    /// <param name="endpoint">Endpoint to test.</param>
    /// <returns><c>true</c> if the endpoint is considered healthy.</returns>
    protected virtual bool IsEndpointHealthy(IEndpointDiagnostics endpoint) =>
        endpoint is { State: ServiceState.Connected or ServiceState.Ok } or
                    { State: null, EndpointState: EndpointState.Connected };

    private static string BuildFailedResultMessage(ServiceType serviceType, HashSet<string> allNodes, HashSet<string> healthyNodes)
    {
        var builder = new StringBuilder();
        builder.Append("Couchbase health check failed for service ");
        builder.Append(serviceType);

        if (allNodes.Count == 0)
        {
            builder.Append(", no nodes available.");
        }
        else if (allNodes.Count == healthyNodes.Count)
        {
            // All nodes are healthy, but didn't meet requirements
            builder.AppendFormat(", {0} healthy nodes.", healthyNodes.Count);
        }
        else
        {
            builder.Append(" for nodes ");

            var enumerator = allNodes.Except(healthyNodes).GetEnumerator();
            try
            {
                if (enumerator.MoveNext())
                {
                    builder.Append(enumerator.Current);

                    while (enumerator.MoveNext())
                    {
                        builder.Append(", ");
                        builder.Append(enumerator.Current);
                    }
                }
            }
            finally
            {
                enumerator?.Dispose();
            }

            builder.Append('.');
        }

        return builder.ToString();
    }
}
