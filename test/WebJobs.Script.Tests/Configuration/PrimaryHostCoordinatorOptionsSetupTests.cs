// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class PrimaryHostCoordinatorOptionsSetupTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Configure_SetsExpectedValues(bool enabled)
        {
            var options = new PrimaryHostCoordinatorOptions
            {
                Enabled = enabled
            };
            var setup = new PrimaryHostCoordinatorOptionsSetup();
            setup.Configure(options);

            // ensure that it's enabled
            Assert.True(options.Enabled);
        }
    }
}
