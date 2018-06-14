// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IScriptHostFactory
    {
        ScriptHost Create();
    }
}
