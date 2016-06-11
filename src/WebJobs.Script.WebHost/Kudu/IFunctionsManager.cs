using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
