// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Config;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretManagerFactory
    {
        ISecretManager Create(ScriptSettingsManager settingsManager, TraceWriter traceWriter, string secretsPath);
    }
}
