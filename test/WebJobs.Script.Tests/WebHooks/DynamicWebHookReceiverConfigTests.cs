// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class DynamicWebHookReceiverConfigTests
    {
        [Theory]
        [InlineData("testfunction,", "DefaultKey")]
        [InlineData("testfunction,Key1", "Value1")]
        [InlineData("testfunction,Key2", "Value2")]
        [InlineData("testfunction,Key3", null)]
        public async Task GetReceiverConfigAsync_ResolvesExpectedSecret(string id, string expected)
        {
            var secretManager = new Mock<ISecretManager>();
            secretManager.Setup(m => m.GetFunctionSecretsAsync("testfunction", true))
                .ReturnsAsync(new Dictionary<string, string>
                {
                    { ScriptConstants.DefaultFunctionKeyName, "DefaultKey" },
                    { "Key1", "Value1" },
                    { "Key2", "Value2" }
                });

            var receiverConfig = new DynamicWebHookReceiverConfig(secretManager.Object);

            var result = await receiverConfig.GetReceiverConfigAsync(string.Empty, id);

            secretManager.VerifyAll();

            Assert.Equal(expected, result);
        }
    }
}
