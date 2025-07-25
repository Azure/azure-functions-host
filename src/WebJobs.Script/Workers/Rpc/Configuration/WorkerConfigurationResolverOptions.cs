// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    public class WorkerConfigurationResolverOptions
    {
        public string ReleaseChannel { get; set; }

        public string WorkerRuntime { get; set; }

        public bool IsPlaceholderModeEnabled { get; set; }

        public bool IsMultiLanguageWorkerEnvironment { get; set; }

        public string WorkersDirPath { get; set; }

        public List<string> ProbingPaths { get; set; }

        public HashSet<string> WorkersAvailableForResolution { get; set; }

        public Dictionary<string, string> LanguageSection { get; set; }

        public string WorkersDir { get; set; }
    }
}