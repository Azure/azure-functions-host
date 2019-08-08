// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.FileProvisioning;
using Microsoft.Azure.WebJobs.Script.FileProvisioning.PowerShell;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.FileAugmentation
{
    public class FuncAppFileProvisionerFactoryTests
    {
        private readonly IFuncAppFileProvisionerFactory _funcAppFileProvisionerFactory;

        public FuncAppFileProvisionerFactoryTests()
        {
            _funcAppFileProvisionerFactory = new FuncAppFileProvisionerFactory(NullLogger<FuncAppFileProvisionerFactory>.Instance);
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
