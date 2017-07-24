// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class LanguageWorkerConfig
    {
        public string ExecutablePath { get; set; }

        public string WorkerPath { get; set; }

        public string Arguments { get; set; }

        public string WorkerId { get; set; }

        public IEnumerable<ScriptType> SupportedScriptTypes { get; set; }

        public string ToArgumentString(int port, string requestId) => $"{WorkerPath} {WorkerId} {Arguments} --host 127.0.0.1 --port {port} --requestId {requestId}";
    }
}
