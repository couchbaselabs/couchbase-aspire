using Couchbase.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Couchbase.HealthChecks;

/// <summary>
/// Represents a health requirement for a Couchbase service.
/// </summary>
public interface ICouchbaseServiceHealthRequirement
{
    /// <summary>
    /// Validates the health requirement against the provided endpoints.
    /// </summary>
    /// <param name="context">Health check context.</param>
    /// <param name="serviceType">Service type being validated.</param>
    /// <param name="endpoints">List of endpoint diagnostics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A health check result for this service requirement.</returns>
    ValueTask<HealthCheckResult> ValidateAsync(HealthCheckContext context, ServiceType serviceType,
        IEnumerable<IEndpointDiagnostics> endpoints, CancellationToken cancellationToken = default);
}
