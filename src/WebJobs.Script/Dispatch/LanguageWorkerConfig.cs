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

        public ICollection<ScriptType> SupportedScriptTypes { get; set; }
    }
}
