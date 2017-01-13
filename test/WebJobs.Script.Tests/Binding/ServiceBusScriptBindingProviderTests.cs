// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Binding.ServiceBus;
using Microsoft.Azure.WebJobs.Script.Config;
using Moq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class ServiceBusScriptBindingProviderTests
    {
        [Fact]
        public static void ServiceBusValidator_Warns_IfDynamicAndListenRights()
        {
            var validator = new Mock<AccessRightsValidator>(null);
            validator.Setup(v => v.TestManageRights()).Throws(new UnauthorizedAccessException());
            var testTraceWriter = new TestTraceWriter(TraceLevel.Warning);
            ServiceBusScriptBindingProvider.ValidateAccessRights(validator.Object, "test", testTraceWriter);

            ScriptSettingsManager.Instance.SetSetting(EnvironmentSettingNames.AzureWebsiteSku, "Dynamic");

            ServiceBusScriptBindingProvider.ValidateAccessRights(validator.Object, "test", testTraceWriter);
            
            Assert.Equal("Service Bus Trigger binding 'test' requires a connection string with Manage AccessRights for correct triggering and scaling behavior when running in a consumption plan.", testTraceWriter.Traces[0].Message);
        }
    }
}
