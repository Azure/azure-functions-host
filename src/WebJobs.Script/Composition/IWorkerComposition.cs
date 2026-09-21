// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.Composition;

/// <summary>
/// Configures worker transport, dispatch, metadata, and lifecycle services for a Functions Host.
/// </summary>
public interface IWorkerComposition
{
    /// <summary>
    /// Adds worker-specific services, MVC application parts, and ScriptHost activation to the root WebHost container.
    /// </summary>
    /// <param name="services">The root WebHost service collection.</param>
    /// <param name="mvcBuilder">The WebHost MVC builder.</param>
    /// <remarks>
    /// Worker lifecycle hosted services must be registered before ScriptHost activation so workers start before the
    /// ScriptHost and stop after it has drained in-flight invocations.
    /// </remarks>
    void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder);

    /// <summary>
    /// Adds worker-specific services to a child ScriptHost container.
    /// </summary>
    /// <param name="services">The child ScriptHost service collection.</param>
    /// <param name="rootServiceProvider">
    /// The root WebHost service provider, used only when a root-owned service must be deliberately forwarded.
    /// </param>
    void ConfigureScriptHostServices(IServiceCollection services, IServiceProvider rootServiceProvider);
}
