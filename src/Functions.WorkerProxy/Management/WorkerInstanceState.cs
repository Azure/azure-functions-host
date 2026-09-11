// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Exposes platform-facing pod state without internal session details or assignment environment values.
/// </summary>
internal sealed record WorkerInstanceState(
    string PodName,
    long RevisionId,
    WorkerPodStateResponse WorkerPodState)
{
    public string FunctionsContainerType => "FunctionsWorkerPod";

    public static WorkerInstanceState FromState(WorkerPodState state) =>
        new(state.PodName, state.Revision, new(state.PodStatus, state.FunctionGroupName, state.IsAlwaysReady));
}
