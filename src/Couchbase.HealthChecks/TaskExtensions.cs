namespace Couchbase.HealthChecks;

/// <summary>
/// Polyfill for Task.WaitAsync.
/// </summary>
internal static class TaskExtensions
{
    /// <summary>
    /// Gets a <see cref="Task{T}"/> that will complete when this <see cref="Task{T}"/> completes,
    /// or when the specified <see cref="CancellationToken"/> has cancellation requested.
    /// </summary>
    public static Task<T> WaitAsync<T>(
        this Task<T> target,
        CancellationToken cancellationToken)
    {
        if (target.IsCompleted ||
            !cancellationToken.CanBeCanceled)
        {
            return target;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        static async Task<T> WaitInternal(Task<T> target, CancellationToken cancellationToken)
        {
            var cancelTask = new TaskCompletionSource<bool>();
            using (cancellationToken.Register(static tcs => ((TaskCompletionSource<bool>)tcs!).TrySetResult(true), cancelTask))
            {
                await Task.WhenAny(target, cancelTask.Task).ConfigureAwait(false);

                if (!target.IsCompleted)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                return target.GetAwaiter().GetResult();
            }
        }

        return WaitInternal(target, cancellationToken);
    }
}
