// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class HostSecretsInfo
    {
        public string MasterKey { get; set; }

        public Dictionary<string, string> FunctionKeys { get; set; }

        public Dictionary<string, string> SystemKeys { get; set; }
    }
}
