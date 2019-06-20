// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public interface IKubernetesClient
    {
        bool IsWritable { get; }

        Task<IDictionary<string, string>> GetSecrets();

        Task UpdateSecrets(IDictionary<string, string> data);

        void OnSecretChange(Action callback);
    }
}
