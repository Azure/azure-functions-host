﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : LanguageWorkerConfig
    {
        public NodeLanguageWorkerConfig()
        {
            ExecutablePath = "node.exe";
            WorkerPath = "workers/node/nodejsWorker.js";

            // TODO set host and port
            Arguments = "--host 127.0.0.1 --port 50051 --requestId requestId";
            SupportedScriptTypes = new List<ScriptType>()
            {
                ScriptType.Javascript
            };
        }
    }
}
