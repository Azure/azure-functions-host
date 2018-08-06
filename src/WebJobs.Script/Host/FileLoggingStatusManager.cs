// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    internal class FileLoggingStatusManager : IFileLoggingStatusManager
    {
        private readonly ScriptJobHostOptions _scriptOptions;
        private readonly IDebugStateProvider _debugStateProvider;

        public FileLoggingStatusManager(IOptions<ScriptJobHostOptions> scriptOptions, IDebugStateProvider debugStateProvider)
        {
            _scriptOptions = scriptOptions.Value;
            _debugStateProvider = debugStateProvider;
        }

        public bool IsFileLoggingEnabled => _scriptOptions.FileLoggingMode == FileLoggingMode.Always ||
                    (_scriptOptions.FileLoggingMode == FileLoggingMode.DebugOnly && _debugStateProvider.InDebugMode);
    }
}
