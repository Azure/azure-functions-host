// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public class ScriptInvocationArguments(IServiceProvider serviceProvider) : Dictionary<string, object>
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
    }
}
