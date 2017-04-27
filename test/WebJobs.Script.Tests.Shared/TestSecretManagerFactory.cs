// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestSecretManagerFactory : ISecretManagerFactory
    {
        private readonly ISecretManager _secretManager;

        public TestSecretManagerFactory(bool cacheResult = true)
        {
            if (cacheResult)
            {
                _secretManager = new SecretManager();
            }
        }

        public TestSecretManagerFactory(ISecretManager secretManager)
        {
            _secretManager = secretManager;
        }

        public ISecretManager Create(ScriptSettingsManager settingsManager, TraceWriter traceWriter, ILoggerFactory loggerFactory, ISecretsRepository secretsRepository) => _secretManager ?? new SecretManager();
    }
}
