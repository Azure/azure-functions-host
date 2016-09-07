// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.WebHooks
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
            // "id" will be a comma delimited string with the function name
            // and an optional client ID. We ignore the "name" parameter since 
            // we only allow a function to be mapped to a single receiver
            string[] webhookIdParts = id.Split(new[] { ',' }, 2, StringSplitOptions.RemoveEmptyEntries);

            Dictionary<string, string> functionSecrets = _secretManager.GetMergedFunctionSecrets(webhookIdParts.FirstOrDefault());

            string clientId = webhookIdParts.Skip(1).FirstOrDefault() ?? SecretManager.DefaultFunctionKeyName;

            string functionSecret = null;
            functionSecrets.TryGetValue(clientId, out functionSecret);

            return Task.FromResult(functionSecret);
        }
    }
}