// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Managment
{
    public class HostAssignmentContextTests
    {
        [Theory]
        [InlineData(null, null, false)]
        [InlineData("", "", false)]
        [InlineData("secret", "", false)]
        [InlineData("", "endpoint", false)]
        [InlineData("secret", "endpoint", true)]
        public void IsMSIEnabled_ReturnsMSIEndpointAndMSIStatus(string msiSecret, string msiEndpoint, bool expectedMsiEnabled)
        {
            var hostAssignmentContext = new HostAssignmentContext();
            hostAssignmentContext.Environment = new Dictionary<string, string>();
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiSecret] = msiSecret;
            hostAssignmentContext.Environment[EnvironmentSettingNames.MsiEndpoint] = msiEndpoint;

            string actualMsiEndpoint;
            var actualMsiEnabled = hostAssignmentContext.IsMSIEnabled(out actualMsiEndpoint);
            Assert.Equal(expectedMsiEnabled, actualMsiEnabled);
            Assert.Equal(msiEndpoint, actualMsiEndpoint);
        }
    }
}
