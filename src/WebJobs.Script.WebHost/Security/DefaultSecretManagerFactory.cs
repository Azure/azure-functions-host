// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public sealed class DefaultSecretManagerFactory : ISecretManagerFactory
    {
        public ISecretManager Create(ScriptSettingsManager settingsManager, TraceWriter traceWriter, string secretsPath)
            => new SecretManager(settingsManager, secretsPath, traceWriter);
    }
}
