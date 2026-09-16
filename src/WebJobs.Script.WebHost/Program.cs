// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script.WebHost;

public class Program
{
    public static async Task Main(string[] args)
    {
        await FunctionsHost.RunAsync(args, ServerWorkerComposition.Instance);
    }

    public static IHost BuildHost(string[] args)
    {
        return FunctionsHost.BuildHost(args, ServerWorkerComposition.Instance);
    }

    /// <summary>
    /// Creates an <see cref="IHostBuilder"/> with only the services the Functions host requires.
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args = null)
    {
        return FunctionsHost.CreateHostBuilder(args, ServerWorkerComposition.Instance);
    }
}
