// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Kudu
{
    public interface IFunctionsManager
    {
        Task<FunctionEnvelope> CreateOrUpdateAsync(string name, FunctionEnvelope functionEnvelope);
        Task<IEnumerable<FunctionEnvelope>> ListFunctionsConfigAsync();
        Task<FunctionEnvelope> GetFunctionConfigAsync(string name);
        Task<FunctionSecrets> GetFunctionSecretsAsync(string name);
        Task<JObject> GetHostConfigAsync();
        Task<JObject> PutHostConfigAsync(JObject content);
        void DeleteFunction(string name);
    }
}