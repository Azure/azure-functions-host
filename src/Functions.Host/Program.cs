// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Azure.Functions.Host;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        await FunctionsHost.RunAsync(args, ClientWorkerComposition.Instance);
    }
}
