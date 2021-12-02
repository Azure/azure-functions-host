// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestSecretManagerProvider : ISecretManagerProvider
    {
        private readonly ISecretManager _secretManager;

        public TestSecretManagerProvider(bool cacheResult = true)
        {
            if (cacheResult)
            {
                _secretManager = new SecretManager();
            }
        }

        public TestSecretManagerProvider(ISecretManager secretManager)
        {
            _secretManager = secretManager;
        }

        public bool SecretsEnabled => true;

        public ISecretManager Current => _secretManager ?? new SecretManager();
    }
}
