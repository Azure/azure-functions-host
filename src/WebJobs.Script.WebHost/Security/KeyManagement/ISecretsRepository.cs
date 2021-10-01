// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretsRepository
    {
        event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        string Name { get; }

        bool IsEncryptionSupported { get; }

        Task<ScriptSecrets> ReadAsync(ScriptSecretsType type, string functionName);

        Task WriteAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets);

        Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, ScriptSecrets secrets);

        Task PurgeOldSecretsAsync(IList<string> currentFunctions, ILogger logger);

        Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName);
    }
}