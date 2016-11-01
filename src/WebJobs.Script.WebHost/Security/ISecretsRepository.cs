// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface ISecretsRepository
    {
        event EventHandler<SecretsChangedEventArgs> SecretsChanged;

        Task<string> ReadAsync(ScriptSecretsType type, string functionName);

        Task WriteAsync(ScriptSecretsType type, string functionName, string secretsContent);

        Task PurgeOldSecretsAsync(IList<string> currentFunctions, TraceWriter traceWriter);
    }
}