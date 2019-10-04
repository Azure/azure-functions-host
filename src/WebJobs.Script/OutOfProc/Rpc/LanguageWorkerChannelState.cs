// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public enum LanguageWorkerChannelState
    {
        /// <summary>
        /// The Default state of LanguageWorkerChannel.
        /// </summary>
        Default,

        /// <summary>
        /// The LanguageWorkerChannel is created.Worker process is starting
        /// </summary>
        Initializing,

        /// <summary>
        /// LanguageWorkerChannel is created. Worker process is Initialized. Rpc Channel is established.
        /// </summary>
        Initialized
    }
}
