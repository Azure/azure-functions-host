// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class KubernetesSecretsRepositoryTests
    {
        [Fact]
        public async Task Read_Write_Functions_Keys()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsKubernetesSecretName, "test");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHost, "127.0.0.1");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.KubernetesServiceHttpsPort, "443");
            IDictionary<string, string> configMapData = new Dictionary<string, string>();
            var clientMock = new Mock<IKubernetesClient>(MockBehavior.Strict);
            clientMock.Setup(c => c.GetSecrets()).ReturnsAsync(configMapData);
            clientMock.SetupGet(c => c.IsWritable).Returns(true);
            clientMock.Setup(c => c.OnSecretChange(It.IsAny<Action>()));
            clientMock.Setup(c => c.UpdateSecrets(It.IsAny<IDictionary<string, string>>())).Returns<IDictionary<string, string>>(a =>
            {
                foreach (var k in a)
                {
                    configMapData[k.Key] = k.Value;
                }
                return Task.CompletedTask;
            });

            var repo = new KubernetesSecretsRepository(environment, clientMock.Object);

            await repo.WriteAsync(ScriptSecretsType.Function, "FUNCTION1", new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    new Key { Name = "Key1", Value = "value" },
                    new Key { Name = "key2", Value = "value" }
                }
            });
            await repo.WriteAsync(ScriptSecretsType.Function, "function2", new FunctionSecrets
            {
                Keys = new List<Key>
                {
                    new Key { Name = "Key1", Value = "value" },
                    new Key { Name = "key2", Value = "value" }
                }
            });

            var result = await repo.ReadAsync(ScriptSecretsType.Function, "function1");
            Assert.NotNull(result);
            Assert.Equal("value", result.GetFunctionKey("Key1", "function1").Value);
            Assert.Equal("value", result.GetFunctionKey("key2", "function1").Value);

            result = await repo.ReadAsync(ScriptSecretsType.Function, "function2");
            Assert.NotNull(result);
            Assert.Equal("value", result.GetFunctionKey("Key1", "function2").Value);
            Assert.Equal("value", result.GetFunctionKey("key2", "function2").Value);
        }
    }
}