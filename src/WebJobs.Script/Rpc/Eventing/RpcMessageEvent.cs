// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Eventing;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public sealed class RpcMessageEvent : ScriptEvent
    {
        public RpcMessageEvent(string name, string source, RpcMessageReceivedEventArgs args) : base(name, source)
        {
            RpcMessageArguments = args;
        }

        public RpcMessageReceivedEventArgs RpcMessageArguments { get; }
    }
}
