// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Eventing
{
    internal class RpcChannelReadyEvent : RpcChannelEvent
    {
        internal RpcChannelReadyEvent(string id, string language, ILanguageWorkerChannel languageWorkerChannel,
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
