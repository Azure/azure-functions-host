// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Dashboard.Data
{
    // Names of directories used only by the dashboard (not part of the protocol with hosts).
    internal static class DashboardDirectoryNames
    {
        public const string AbortRequestLogs = "aborts";

        public const string Functions = "functions";
        public const string FunctionsFlat = "functions/flat";
        public const string FunctionStatistics = "functions/stats";
        public const string FunctionInstances = "functions/instances";

        public const string Hosts = "hosts";

        public const string RecentFunctionsByFunction = "functions/recent/by-function";
        public const string RecentFunctionsByJobRun = "functions/recent/by-job-run";
        public const string RecentFunctionsByParent = "functions/recent/by-parent";
        public const string RecentFunctionsFlat = "functions/recent/flat";

        public const string Logs = "logs";
        public const string IndexerLog = Logs + "/indexer";

        /// <summary>The name of the container where version compatibility warnings are stored.</summary>
        public const string Versions = "versions";
    }
}
