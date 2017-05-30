// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : LanguageWorkerConfig
    {
        public NodeLanguageWorkerConfig()
        {
            ExecutablePath = "node.exe";
            WorkerPath = "workers/node/nodejsWorker.js";
            Arguments = string.Empty;
            SupportedScriptTypes = new List<ScriptType>()
            {
                ScriptType.Javascript
            };
        }
    }
}
