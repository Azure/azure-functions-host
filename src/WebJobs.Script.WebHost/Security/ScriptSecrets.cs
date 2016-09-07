// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public abstract class ScriptSecrets
    {
        protected ScriptSecrets()
        {
        }

        [JsonIgnore]
        public abstract bool HasStaleKeys { get; }

        public abstract ScriptSecrets Refresh(IKeyValueConverterFactory factory);
    }
}
