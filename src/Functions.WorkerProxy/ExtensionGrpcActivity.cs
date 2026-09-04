using System.Diagnostics;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Enriches worker-facing ASP.NET Core activities with extension RPC correlation and concurrency data.
/// </summary>
internal static class ExtensionGrpcActivity
{
    private const string TagPrefix = "azure.functions.worker_proxy.extension_rpc";

    /// <summary>
    /// Enriches the current activity after an extension call acquires a host stream.
    /// </summary>
    /// <param name="callId">The logical extension call identifier.</param>
    /// <param name="streamId">The physical extension stream identifier.</param>
    /// <param name="activeCallCount">The active-call count when the call opens.</param>
    /// <returns>The enriched activity, or <see langword="null"/> when no activity is active.</returns>
    public static void CallOpened(string callId, string streamId, int activeCallCount)
    {
        Activity? activity = Activity.Current;
        activity?.SetTag($"{TagPrefix}.call_id", callId);
        activity?.SetTag($"{TagPrefix}.stream_id", streamId);
        activity?.SetTag($"{TagPrefix}.active_calls_at_open", activeCallCount);
    }

    /// <summary>
    /// Enriches an extension call activity with its final concurrency snapshot.
    /// </summary>
    /// <param name="activity">The activity captured when the call opened.</param>
    /// <param name="activeCallCount">The active-call count when the call completes.</param>
    public static void CallCompleted(int activeCallCount)
    {
        Activity? activity = Activity.Current;
        activity?.SetTag($"{TagPrefix}.active_calls_at_completion", activeCallCount);
    }
}
