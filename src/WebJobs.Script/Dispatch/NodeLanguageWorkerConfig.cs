// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : LanguageWorkerConfig
    {
        public NodeLanguageWorkerConfig()
        {
            ExecutablePath = "node.exe";
            WorkerPath = Environment.GetEnvironmentVariable("NodeJSWorkerPath");

            SupportedScriptTypes = new List<ScriptType>()
            {
                ScriptType.Javascript
            };
        }
    }
}
