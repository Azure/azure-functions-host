// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace WorkerHarness.Core.Actions
{
    /// <summary>
    /// Encapsulate information about an action
    /// </summary>
    internal class RpcActionData
    {
        // the type of an action
        public string ActionType { get; } = ActionTypes.Rpc;

        // the name of an action
        public string ActionName { get; set; } = ActionTypes.Rpc;

        // the amount of time to execute an action; default to 5s timeout
        public int Timeout { get; set; } = 5000;

        public IEnumerable<RpcActionMessage> Messages { get; set; } = new List<RpcActionMessage>();

    }
}
