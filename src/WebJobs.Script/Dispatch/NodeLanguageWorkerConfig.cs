// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class NodeLanguageWorkerConfig : LanguageWorkerConfig
    {
        public NodeLanguageWorkerConfig()
        {
            ExecutablePath = "node";
            WorkerPath = Environment.GetEnvironmentVariable("NodeJSWorkerPath");
            ScriptType = ScriptType.Javascript;
            Extension = ".js";
        }
    }
}
