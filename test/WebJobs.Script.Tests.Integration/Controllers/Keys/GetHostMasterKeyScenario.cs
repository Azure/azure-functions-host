// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Net.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Controllers
{
    [Trait("A GET request is made against the host master key resource endpoint", "")]
    public class GetHostMasterKeyScenario : GetHostSystemKeyScenario,  IClassFixture<GetHostMasterKeyScenario.HostMasterKeyFixture>
    {
        public GetHostMasterKeyScenario(HostMasterKeyFixture fixture)
            : base(fixture)
        {
        }

        public class HostMasterKeyFixture : SystemKeyFixture
        {
            public override string KeyName => "_master";

            public override string KeyValue => MasterKey;

            protected override Mock<TestSecretManager> BuildSecretManager()
            {
                Mock<TestSecretManager> manager = base.BuildSecretManager();

                manager.Setup(s => s.GetHostSecretsAsync())
                 .ReturnsAsync(() => new HostSecretsInfo
                 {
                     MasterKey = MasterKey
                 });

                return manager;
            }
        }
    }
}