using Aspire.Hosting;
using Microsoft.Extensions.Hosting;

namespace Couchbase.Aspire.Hosting.Orchestration;

internal sealed class CouchbaseOrchestratorService(
    CouchbaseClusterOrchestrator orchestrator,
    DistributedApplicationExecutionContext executionContext)
    : IHostedLifecycleService
{
    private bool IsSupported => !executionContext.IsPublishMode;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return;
        }

        // The orchestrator must be started before DCP because it needs to handle events fired during
        // DCP startup and additional hosted services do not start until after all services are running.
        // Therefore, start the orchestrator in StartingAsync rather than StartAsync.
        await orchestrator.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
