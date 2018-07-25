// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostExtensions
    {
        public static ScriptHost GetScriptHost(this IHost host)
        {
            return host.Services.GetService<IScriptJobHost>() as ScriptHost;
        }
    }
}
