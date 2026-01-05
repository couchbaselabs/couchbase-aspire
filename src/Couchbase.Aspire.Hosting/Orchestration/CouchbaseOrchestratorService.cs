using Aspire.Hosting;
using Microsoft.Extensions.Hosting;

namespace Couchbase.Aspire.Hosting.Orchestration;

internal sealed class CouchbaseOrchestratorService(
    CouchbaseClusterOrchestrator orchestrator,
    DistributedApplicationExecutionContext executionContext)
    : IHostedLifecycleService
{
    private bool IsSupported => !executionContext.IsPublishMode;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return;
        }

        await orchestrator.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await orchestrator.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task StartedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
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
