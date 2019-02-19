// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    /// <summary>
    /// RpcWebHostChannelReadyEvent is published when a language worker channel is started at the
    /// Webhost/Application level. LanguageWorkerChannelManager keeps track that channel created
    /// </summary>
    internal class RpcWebHostChannelReadyEvent : RpcChannelEvent
    {
        internal RpcWebHostChannelReadyEvent(string id, string language, ILanguageWorkerChannel languageWorkerChannel,
            string version, IDictionary<string, string> capabilities)
            : base(id)
        {
            Language = language;
            LanguageWorkerChannel = languageWorkerChannel;
            Version = version;
            Capabilities = capabilities;
        }

        public string Language { get; }

        public ILanguageWorkerChannel LanguageWorkerChannel { get; }

        public string Version { get; }

        public IDictionary<string, string> Capabilities { get; }
    }
}
