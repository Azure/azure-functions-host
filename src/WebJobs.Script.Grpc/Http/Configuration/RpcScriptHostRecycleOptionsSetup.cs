// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Rpc.Configuration;

internal class RpcScriptHostRecycleOptionsSetup : IConfigureOptions<ScriptHostRecycleOptions>
{
    private readonly IOptions<HttpWorkerOptions> _httpOptions;

    public RpcScriptHostRecycleOptionsSetup(IOptions<HttpWorkerOptions> httpOptions)
    {
        _httpOptions = httpOptions;
    }

    public void Configure(ScriptHostRecycleOptions options)
    {
        if (_httpOptions.Value.IsPortManuallySet)
        {
            options.SequentialHostRestartRequired = true;
        }
    }
}
