// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Autofac;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Moq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    public class KeyManagementFixture : ControllerScenarioTestFixture
    {
        private readonly string _testFunctionName = "httptrigger-csharp";

        public Dictionary<string, string> TestFunctionKeys { get; set; }

        public Dictionary<string, string> TestSystemKeys { get; set; }

        public Mock<TestSecretManager> SecretManagerMock { get; set; }

        public virtual string TestKeyScope => _testFunctionName;

        public virtual ScriptSecretsType SecretsType => ScriptSecretsType.Function;

        protected override void RegisterDependencies(ContainerBuilder builder, WebHostSettings settings)
        {
            TestFunctionKeys = new Dictionary<string, string>
            {
                { "key1", "1234" },
                { "key2", "1234" }
            };

            SecretManagerMock = BuildSecretManager();

            builder.RegisterInstance<ISecretManager>(SecretManagerMock.Object);

            base.RegisterDependencies(builder, settings);
        }

        protected virtual Mock<TestSecretManager> BuildSecretManager()
        {
            var manager = new Mock<TestSecretManager>();
            manager.CallBase = true;
            manager.Setup(s => s.GetFunctionSecretsAsync(_testFunctionName, false))
                .ReturnsAsync(() => TestFunctionKeys);

            return manager;
        }
    }
}