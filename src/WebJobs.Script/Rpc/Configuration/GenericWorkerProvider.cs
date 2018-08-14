// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    internal class GenericWorkerProvider : IWorkerProvider
    {
        private WorkerDescription _workerDescription;
        private string _pathToWorkerDir;

        public GenericWorkerProvider(WorkerDescription workerDescription, string pathToWorkerDir)
        {
            _workerDescription = workerDescription ?? throw new ArgumentNullException(nameof(workerDescription));
            _pathToWorkerDir = pathToWorkerDir ?? throw new ArgumentNullException(nameof(pathToWorkerDir));
        }

        public WorkerDescription GetDescription()
        {
            return _workerDescription;
        }

        public bool TryConfigureArguments(WorkerProcessArguments args, ILogger logger)
        {
            if (_workerDescription.Arguments != null)
            {
                args.ExecutableArguments.AddRange(_workerDescription.Arguments);
            }
            return true;
        }
    }
}
