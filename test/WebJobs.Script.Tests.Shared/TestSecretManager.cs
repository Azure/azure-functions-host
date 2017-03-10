// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestSecretManager : ISecretManager
    {        
        public virtual Task PurgeOldSecretsAsync(string rootScriptPath, TraceWriter traceWriter)
        {
            throw new NotImplementedException();
        }

        public virtual Task<bool> DeleteSecretAsync(string secretName, string keyScope, ScriptSecretsType secretsType)
        {
            return Task.FromResult(true);
        }

        public virtual Task<IDictionary<string, string>> GetFunctionSecretsAsync(string functionName, bool merged)
        {
            return Task.FromResult<IDictionary<string, string>>(new Dictionary<string, string>
            {
                { "Key1", "Value1" },
                { "Key2", "Value2" },
            });
        }

        public virtual Task<HostSecretsInfo> GetHostSecretsAsync()
        {
            return Task.FromResult(new HostSecretsInfo
            {
                MasterKey = "1234",
                FunctionKeys = new Dictionary<string, string>
                {
                    { "HostKey1", "HostValue1" },
                    { "HostKey2", "HostValue2" },
                }
            });
        }

        public virtual Task<KeyOperationResult> AddOrUpdateFunctionSecretAsync(string secretName, string secret, string keyScope, ScriptSecretsType secretsType)
        {
            string resultSecret = secret ?? "generated";
            return Task.FromResult(new KeyOperationResult(resultSecret, OperationResult.Created));
        }

        public virtual Task<KeyOperationResult> SetMasterKeyAsync(string value)
        {
            throw new NotImplementedException();
        }
    }
}
