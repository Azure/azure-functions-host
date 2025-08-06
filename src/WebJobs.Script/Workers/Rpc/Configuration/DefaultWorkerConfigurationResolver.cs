// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using Microsoft.Azure.WebJobs.Script.Diagnostics.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration
{
    // This class resolves worker configurations by scanning the "workers" directory within the Host for worker config files.
    internal sealed class DefaultWorkerConfigurationResolver : IWorkerConfigurationResolver
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<WorkerConfigurationResolverOptions> _workerConfigurationResolverOptions;
        private readonly IFileSystem _fileSystem;

        public DefaultWorkerConfigurationResolver(ILoggerFactory loggerFactory,
                                                    IFileSystem fileSystem,
                                                    IOptionsMonitor<WorkerConfigurationResolverOptions> workerConfigurationResolverOptions)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryWorkerConfig);
            _workerConfigurationResolverOptions = workerConfigurationResolverOptions ?? throw new ArgumentNullException(nameof(workerConfigurationResolverOptions));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public IEnumerable<string> GetWorkerConfigPaths()
        {
            var workersDirPath = _workerConfigurationResolverOptions.CurrentValue.WorkersDirPath;
            _logger.DefaultWorkersDirectoryPath(workersDirPath);

            foreach (var workerDir in _fileSystem.Directory.EnumerateDirectories(workersDirPath))
            {
                string workerConfigPath = _fileSystem.Path.Combine(workerDir, RpcWorkerConstants.WorkerConfigFileName);

                if (_fileSystem.File.Exists(workerConfigPath))
                {
                    yield return workerDir;
                }
            }
        }

        public WorkerConfigurationResolutionInfo GetWorkerConfigurationResolutionInfo()
        {
            return new WorkerConfigurationResolutionInfo(_workerConfigurationResolverOptions.CurrentValue.WorkersDirPath);
        }
    }
}
