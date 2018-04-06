// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestWorkerProvider : IWorkerProvider
    {
        public string Language { get; set; }

        public string Extension { get; set; }

        public string DefaultWorkerPath { get; set; }

        public WorkerDescription GetDescription() => new WorkerDescription
        {
            Language = this.Language,
            Extension = this.Extension,
            DefaultWorkerPath = this.DefaultWorkerPath,
        };

        public string GetWorkerDirectoryPath()
        {
            return string.Empty;
        }

        public bool TryConfigureArguments(ArgumentsDescription args, IConfiguration config, ILogger logger)
        {
            // make no modifications
            return true;
        }
    }
}
