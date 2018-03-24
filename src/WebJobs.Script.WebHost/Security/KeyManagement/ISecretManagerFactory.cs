// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretManagerFactory
    {
        ISecretManager Create(ScriptSettingsManager settingsManager, ILogger logger, ISecretsRepository secretsRepository);
    }
}
