// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class WorkerProviderHelper
    {
        public static string BuildWorkerDirectoryPath(string languageName)
        {
            return Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), ScriptConstants.DefaultWorkersDirectoryName, languageName);
        }
    }
}
