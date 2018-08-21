// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ScriptWebHostEnvironmentTests
    {
        [Fact]
        public void InStandbyMode_ReturnsExpectedValue()
        {
            var environment = new Tests.TestEnvironment();
            var mockStandbyManager = new Mock<IStandbyManager>();
            var scriptHostEnvironment = new ScriptWebHostEnvironment(environment);

            // initially false
            Assert.Equal(false, scriptHostEnvironment.InStandbyMode);

            scriptHostEnvironment = new ScriptWebHostEnvironment(environment);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.Equal(true, scriptHostEnvironment.InStandbyMode);

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");
            Assert.Equal(false, scriptHostEnvironment.InStandbyMode);

            // test only set one way
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            Assert.Equal(false, scriptHostEnvironment.InStandbyMode);
        }
    }
}
