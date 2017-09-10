// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;

namespace Microsoft.Azure.WebJobs.Script.Abstractions.Rpc
{
    public class WorkerConfig
    {
        public string ExecutablePath { get; set; }

        public IDictionary<string, string> ExecutableArguments { get; set; } = new Dictionary<string, string>();

        public IDictionary<string, string> WorkerArguments { get; set; } = new Dictionary<string, string>();

        public string WorkerPath { get; set; }

        public string Extension { get; set; }

        public string Language { get; set; }

        protected string Location => Path.GetDirectoryName(new Uri(typeof(WorkerConfig).Assembly.CodeBase).LocalPath);
    }
}
