// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Azure.Functions.Rpc.Client.Tests;

public partial class RpcClientScriptHostStartupIntegrationTests
{
    [TriggerLifecycleFact("BYOC_TEST_STORAGE_CONNECTION")]
    public async Task TimerListener_MatchingWorkersGateRecurringTicks_DrainAndRestart()
    {
        string functionName = $"Timer{Guid.NewGuid():N}";
        await VerifyTriggerListenerLifecycleAsync(
            functionName,
            """{"name":"timer","type":"timerTrigger","direction":"in","schedule":"*/1 * * * * *","useMonitor":true}""",
            builder => builder.AddTimers(),
            new Dictionary<string, string>
            {
                ["AzureWebJobsStorage"] = Environment.GetEnvironmentVariable("BYOC_TEST_STORAGE_CONNECTION")!,
            },
            publishAsync: null);
    }
}
