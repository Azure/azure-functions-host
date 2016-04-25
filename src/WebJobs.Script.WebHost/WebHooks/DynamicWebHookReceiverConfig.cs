// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNet.WebHooks;

namespace WebJobs.Script.WebHost.WebHooks
{
    public class DynamicWebHookReceiverConfig : IWebHookReceiverConfig
    {
        private readonly SecretManager _secretManager;

        public DynamicWebHookReceiverConfig(SecretManager secretManager)
        {
            _secretManager = secretManager;
        }

        public Task<string> GetReceiverConfigAsync(string name, string id)
        {
            string functionName = id;
            string keyId = null;
            int idx = id.IndexOf(':');
            if (idx > 0)
            {
                functionName = id.Substring(0, idx);
                keyId = id.Substring(idx + 1);
            }

            // "id" will be the function name
            // we ignore the "name" parameter since we only allow a function
            // to be mapped to a single receiver
            FunctionSecrets secrets = _secretManager.GetFunctionSecrets(functionName);
            if (secrets != null)
            {
                string key = secrets.GetKeyValue(keyId);
                return Task.FromResult(key);
            }

            return null;
        }
    }
}