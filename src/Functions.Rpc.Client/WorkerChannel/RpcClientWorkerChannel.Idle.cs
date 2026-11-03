// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client;

internal sealed partial class RpcClientWorkerChannel
{
    private TimeProvider _activityTimeProvider = TimeProvider.System;
    private HashSet<string> _pendingFunctionLoads = [];
    private long? _idleSince;
    private bool _retiring;

    // Unknown metadata is not evidence that an assigned worker is on-demand.
    internal bool? IsAlwaysReady { get; private set; }

    internal int OngoingRequests => TrackedInvocationCount;

    internal void StartIdleTracking(IReadOnlyList<FunctionMetadata> functions, TimeProvider timeProvider)
    {
        lock (_lifecycleLock)
        {
            _activityTimeProvider = timeProvider;
            _pendingFunctionLoads = functions.Select(function => function.GetFunctionId()).ToHashSet(StringComparer.Ordinal);
            _idleSince = null;
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
        if (_pendingFunctionLoads.Count == 0 && !HasTrackedInvocations && _idleSince is null)
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

            PostInvocation(context, trackRequestLifetime: IsHttpFunctionGroup);
            return true;
        }
    }

    protected override void OnInvocationActivityChanged()
    {
        lock (_lifecycleLock)
        {
            if (HasTrackedInvocations)
            {
                _idleSince = null;
            }
            else
            {
                RecordReadyIdleTime();
            }
        }
    }

    internal bool TryClaimIdleRetirement(TimeSpan threshold, out TimeSpan idleDuration)
    {
        lock (_lifecycleLock)
        {
            idleDuration = _idleSince is long since ? _activityTimeProvider.GetElapsedTime(since) : TimeSpan.Zero;
            if (!IsHttpFunctionGroup || IsAlwaysReady is not false || !IsReadyForInvocations ||
                _pendingFunctionLoads.Count != 0 || HasTrackedInvocations || idleDuration <= threshold)
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
