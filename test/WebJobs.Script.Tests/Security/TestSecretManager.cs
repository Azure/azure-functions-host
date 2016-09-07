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
        public virtual bool DeleteSecret(string secretName, string functionName = null)
        {
            return true;
        }

        public virtual IDictionary<string, string> GetFunctionSecrets(string functionName, bool merged = false)
        {
            return new Dictionary<string, string>
            {
                { "Key1", "Value1" },
                { "Key2", "Value2" },
            };
        }

        public virtual HostSecretsInfo GetHostSecrets()
        {
            return new HostSecretsInfo
            {
                MasterKey = "1234",
                FunctionKeys = new Dictionary<string, string>
                {
                    { "HostKey1", "HostValue1" },
                    { "HostKey2", "HostValue2" },
                }
            };
        }

        public virtual void PurgeOldFiles(string rootScriptPath, TraceWriter traceWriter)
        {
        }

        public virtual KeyOperationResult AddOrUpdateFunctionSecret(string secretName, string secret, string functionName = null)
        {
            string resultSecret = secret ?? "generated";
            return new KeyOperationResult(resultSecret, OperationResult.Created);
        }

        public KeyOperationResult SetMasterKey(string value = null)
        {
            throw new NotImplementedException();
        }
    }
}
