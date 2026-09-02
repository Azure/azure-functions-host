// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Functions.Host;

/// <summary>
/// Defines the Client-backed worker composition for the separate Functions Host.
/// </summary>
/// <remarks>
/// Client-backed registrations land in later milestones. The throwing methods make the intentionally incomplete
/// boundary visible until those registrations are supplied.
/// </remarks>
internal sealed class ClientWorkerComposition : IWorkerComposition
{
    private ClientWorkerComposition()
    {
    }

    public static ClientWorkerComposition Instance { get; } = new();

    public void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder)
    {
        throw new NotImplementedException("Client WebHost worker composition is not implemented.");
    }

    public void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider)
    {
        throw new NotImplementedException("Client ScriptHost worker composition is not implemented.");
    }
}
