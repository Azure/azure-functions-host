// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestSecretManager : ISecretManager
    {
        internal const string TestMasterKey = "1234";
        private Dictionary<string, string> _hostSystemKeys;
        private Dictionary<string, string> _hostFunctionKeys;

        public TestSecretManager()
        {
            Reset();
        }

        public virtual Task PurgeOldSecretsAsync(string rootScriptPath, ILogger logger)
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
                { "Key1", $"{functionName}1".ToLowerInvariant() },
                { "Key2", $"{functionName}2".ToLowerInvariant() },
            });
        }

        public virtual Task<HostSecretsInfo> GetHostSecretsAsync()
        {
            return Task.FromResult(new HostSecretsInfo
            {
                MasterKey = TestMasterKey,
                FunctionKeys = _hostFunctionKeys,
                SystemKeys = _hostSystemKeys
            });
        }

        public virtual Task<KeyOperationResult> AddOrUpdateFunctionSecretAsync(string secretName, string secret, string keyScope, ScriptSecretsType secretsType)
        {
            if (secretsType == ScriptSecretsType.Host)
            {
                if (keyScope == HostKeyScopes.SystemKeys)
                {
                    _hostSystemKeys[secretName] = secret;
                }
                else if (keyScope == HostKeyScopes.FunctionKeys)
                {
                    _hostFunctionKeys[secretName] = secret;
                }
            }

            string resultSecret = secret ?? "generated";
            return Task.FromResult(new KeyOperationResult(resultSecret, OperationResult.Created));
        }

        public virtual Task<KeyOperationResult> SetMasterKeyAsync(string value)
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            _hostFunctionKeys = new Dictionary<string, string>
                {
                    { "HostKey1", "HostValue1" },
                    { "HostKey2", "HostValue2" },
                };

            _hostSystemKeys = new Dictionary<string, string>
                {
                    { "SystemKey1", "SystemValue1" },
                    { "SystemKey2", "SystemValue2" },
                    { "Test_Extension", "SystemValue3" },
                };
        }
    }
}
