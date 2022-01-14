// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.IO.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    public class LinuxAppServiceFileLoggerFactory
    {
        private static readonly ConcurrentDictionary<string, Lazy<LinuxAppServiceFileLogger>> Loggers = new ConcurrentDictionary<string, Lazy<LinuxAppServiceFileLogger>>();
        private readonly string _logRootPath;

        public LinuxAppServiceFileLoggerFactory()
        {
            _logRootPath = Environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsLogsMountPath);
        }

        public virtual LinuxAppServiceFileLogger GetOrCreate(string category)
        {
            return Loggers.GetOrAdd(category,
                static (c, path) => new Lazy<LinuxAppServiceFileLogger>(() => new LinuxAppServiceFileLogger(c, path, new FileSystem())), _logRootPath).Value;
        }
    }
}
