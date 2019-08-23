// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.FileProvisioning;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class FuncAppFileProvisionerFactoryTests
    {
        private readonly IFuncAppFileProvisionerFactory _funcAppFileProvisionerFactory;
        private readonly ILoggerFactory _loggerFactory;

        public FuncAppFileProvisionerFactoryTests()
        {
            _loggerFactory = new LoggerFactory();
            _funcAppFileProvisionerFactory = new FuncAppFileProvisionerFactory(_loggerFactory);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("dotnet")]
        [InlineData("powershell")]
        public void CreatFileProvisioner_Test(string runtime)
        {
            var fileProvisioner = _funcAppFileProvisionerFactory.CreatFileProvisioner(runtime);
            if (string.Equals(runtime, "powershell", StringComparison.InvariantCultureIgnoreCase))
            {
                Assert.True(fileProvisioner != null);
                Assert.True(fileProvisioner is PowerShellFileProvisioner);
            }
            else
            {
                Assert.True(fileProvisioner == null);
            }
        }
    }
}
