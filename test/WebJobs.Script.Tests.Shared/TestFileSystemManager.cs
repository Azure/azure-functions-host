// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestFileSystemManager : IFileSystemManager
    {
        private static IFileSystemManager _fileSystemManager;
        private static ILogger _logger = new TestLogger("TestFileSystemManager");

        public TestFileSystemManager()
        {
            _fileSystemManager = new FileSystemManager(new TestEnvironment(), _logger);
        }

        public TestFileSystemManager(IEnvironment environment)
        {
            _fileSystemManager = new FileSystemManager(environment, _logger);
        }

        public void UpdateEnvironment(IEnvironment environment)
        {
            _fileSystemManager = new FileSystemManager(environment, _logger);
        }

        public void CacheIfBlobExists()
        {
            throw new NotImplementedException();
        }

        public bool IsFileSystemReadOnly()
        {
            return IsZipDeployment();
        }

        public bool IsZipDeployment(bool validate = true)
        {
            return _fileSystemManager.IsZipDeployment(validate);
        }
    }
}
