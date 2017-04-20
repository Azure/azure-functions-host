// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    [CLSCompliant(false)]
    public interface ISecretManagerFactory
    {
        ISecretManager Create(ScriptSettingsManager settingsManager, TraceWriter traceWriter, ILoggerFactory loggerFactory, ISecretsRepository secretsRepository);
    }
}
