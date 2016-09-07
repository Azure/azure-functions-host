// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IScriptSecretSerializer
    {
        FunctionSecrets DeserializeFunctionSecrets(JObject secrets);

        string SerializeFunctionSecrets(FunctionSecrets secrets);

        HostSecrets DeserializeHostSecrets(JObject secrets);

        string SerializeHostSecrets(HostSecrets secrets);

        bool CanSerialize(JObject functionSecrets, SecretsType type);
    }
}
