// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc
{
    [Flags]
    public enum RpcWorkerChannelState
    {
        /// <summary>
        /// The Default state of LanguageWorkerChannel.
        /// </summary>
        Default = 1 << 0,

        /// <summary>
        /// LanguageWorkerChannel is created. InvocationBuffers per function are setup
        /// </summary>
        InvocationBuffersInitialized = 1 << 1,

        /// <summary>
        /// The LanguageWorkerChannel is created. Worker process is starting
        /// </summary>
        Initializing = 1 << 2,

        /// <summary>
        /// LanguageWorkerChannel is created. Worker process is Initialized. Rpc Channel is established.
        /// </summary>
        Initialized = 1 << 3,
    }
}
