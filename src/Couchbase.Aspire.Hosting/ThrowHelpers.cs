using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Couchbase.Aspire.Hosting;

internal static class ThrowHelpers
{
    public static string ThrowIfNullOrEmpty([NotNull] string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(argument, paramName);
        return argument;
    }
}
