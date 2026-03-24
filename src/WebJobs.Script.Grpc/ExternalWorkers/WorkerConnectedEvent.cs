// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Published when a connected external worker completes the WorkerInitResponse handshake.
/// </summary>
internal class WorkerConnectedEvent : ScriptEvent
{
    public WorkerConnectedEvent(string workerId, string runtime)
        : base(nameof(WorkerConnectedEvent), EventSources.Rpc)
    {
        WorkerId = workerId;
        Runtime = runtime;
    }

    public string WorkerId { get; }

    public string Runtime { get; }
}
