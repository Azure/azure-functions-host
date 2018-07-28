// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretManagerFactory : ISecretManagerFactory
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly ILogger _logger;
        private readonly ISecretsRepository _secretsRepository;

        public DefaultSecretManagerFactory(ScriptSettingsManager settingsManager, ILoggerFactory loggerFactory, ISecretsRepository secretsRepository)
        {
            if (loggerFactory == null)
            {
                throw new System.ArgumentNullException(nameof(loggerFactory));
            }

            _settingsManager = settingsManager ?? throw new System.ArgumentNullException(nameof(settingsManager));
            _logger = loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral);
            _secretsRepository = secretsRepository ?? throw new System.ArgumentNullException(nameof(secretsRepository));
        }

        public ISecretManager Create()
            => new SecretManager(_secretsRepository, _logger);
    }
}
