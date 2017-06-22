// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class ScriptHostProvider
    {
        private readonly IServiceProvider _serviceProvider;

        public ScriptHostProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public ScriptHost ResolveHost()
        {
            return _serviceProvider
                .GetService<WebHostResolver>()
                ?.GetWebScriptHostManager()
                .Instance;
        }
    }
}
