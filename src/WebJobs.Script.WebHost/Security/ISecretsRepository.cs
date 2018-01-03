// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretsRepository
    {
        event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        Task<string> ReadAsync(ScriptSecretsType type, string functionName);

        Task WriteAsync(ScriptSecretsType type, string functionName, string secretsContent);

        Task WriteSnapshotAsync(ScriptSecretsType type, string functionName, string secretsContent);

        Task PurgeOldSecretsAsync(IList<string> currentFunctions, TraceWriter traceWriter, ILogger logger);

        Task<string[]> GetSecretSnapshots(ScriptSecretsType type, string functionName);
    }
}