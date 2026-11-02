// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.Rpc.Client;

internal sealed partial class RpcClientWorkerChannel
{
    private TimeProvider _activityTimeProvider = TimeProvider.System;
    private HashSet<string> _pendingFunctionLoads = [];
    private long? _idleSince;
    private int _ongoingRequests;
    private bool _setupCompleted;
    private bool _retiring;

    internal bool IsHttpFunctionGroup { get; private set; }

    // Unknown metadata is not evidence that an assigned worker is on-demand.
    internal bool? IsAlwaysReady { get; private set; }

    internal int OngoingRequests
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _ongoingRequests;
            }
        }
    }

    internal bool IsIdleTrackingReady
    {
        get
        {
            lock (_lifecycleLock)
            {
                return _setupCompleted && _pendingFunctionLoads.Count == 0;
            }
        }
    }

    internal void PrepareIdleTracking(IReadOnlyList<FunctionMetadata> functions, TimeProvider timeProvider)
    {
        lock (_lifecycleLock)
        {
            _activityTimeProvider = timeProvider;
            _pendingFunctionLoads = functions.Select(function => function.GetFunctionId()).ToHashSet(StringComparer.Ordinal);
            _setupCompleted = false;
            _idleSince = null;
        }
    }

    internal void CompleteIdleTrackingSetup()
    {
        lock (_lifecycleLock)
        {
            _setupCompleted = true;
            RecordReadyIdleTime();
        }
    }

    protected override void OnFunctionLoaded(FunctionLoadResponse response)
    {
        lock (_lifecycleLock)
        {
            if (response.Result.Status is StatusResult.Types.Status.Success)
            {
                _pendingFunctionLoads.Remove(response.FunctionId);
                RecordReadyIdleTime();
            }
        }
    }

    private void RecordReadyIdleTime()
    {
        if (_setupCompleted && _pendingFunctionLoads.Count == 0 && _ongoingRequests == 0 && _idleSince is null)
        {
            _idleSince = _activityTimeProvider.GetTimestamp();
        }
    }

    internal bool TryPostInvocation(ScriptInvocationContext context)
    {
        lock (_lifecycleLock)
        {
            if (!IsReadyForInvocations)
            {
                return false;
            }

            if (!FunctionInputBuffers.TryGetValue(context.FunctionMetadata.GetFunctionId(), out BufferBlock<ScriptInvocationContext> buffer))
            {
                throw new InvalidOperationException($"Function '{context.FunctionMetadata.Name}' is not loaded by worker '{Id}'.");
            }

            bool track = IsHttpFunctionGroup;
            Task responseCompleted = Task.CompletedTask;
            if (track)
            {
                if (context.Inputs?.Select(input => input.Val).OfType<HttpRequest>().FirstOrDefault() is HttpRequest request)
                {
                    TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    request.HttpContext.Response.OnCompleted(() =>
                    {
                        completion.TrySetResult();
                        return Task.CompletedTask;
                    });
                    responseCompleted = completion.Task;
                }

                _ongoingRequests++;
                _idleSince = null;
            }

            if (!buffer.Post(context))
            {
                if (track)
                {
                    CompleteRequest();
                }

                throw new InvalidOperationException($"Worker '{Id}' rejected the invocation input.");
            }

            if (track)
            {
                _ = ObserveRequestCompletionAsync(context, responseCompleted);
            }

            return true;
        }
    }

    private async Task ObserveRequestCompletionAsync(ScriptInvocationContext context, Task responseCompleted)
    {
        try
        {
            try
            {
                await context.ResultSource.Task.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // The invoker owns the result failure; accounting must still wait for any active forwarder.
                _activityLogger.LogDebug(exception, "Invocation on worker {WorkerId} ended unsuccessfully.", Id);
            }

            if (context.Properties.TryGetValue(ScriptConstants.HttpProxyTask, out object value) && value is Task forwarding)
            {
                try
                {
                    await forwarding.ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _activityLogger.LogDebug(exception, "HTTP forwarding on worker {WorkerId} ended unsuccessfully.", Id);
                }
            }

            await responseCompleted.ConfigureAwait(false);
        }
        finally
        {
            lock (_lifecycleLock)
            {
                CompleteRequest();
            }
        }
    }

    private void CompleteRequest()
    {
        _ongoingRequests--;
        RecordReadyIdleTime();
    }

    internal bool TryClaimIdleRetirement(TimeSpan threshold, out TimeSpan idleDuration)
    {
        lock (_lifecycleLock)
        {
            idleDuration = _idleSince is long since ? _activityTimeProvider.GetElapsedTime(since) : TimeSpan.Zero;
            if (!IsHttpFunctionGroup || IsAlwaysReady is not false || !IsReadyForInvocations ||
                !_setupCompleted || _pendingFunctionLoads.Count != 0 || _ongoingRequests != 0 || idleDuration <= threshold)
            {
                return false;
            }

            _retiring = true;
            return true;
        }
    }

    internal void StopAcceptingInvocations()
    {
        lock (_lifecycleLock)
        {
            _retiring = true;
        }
    }
}
