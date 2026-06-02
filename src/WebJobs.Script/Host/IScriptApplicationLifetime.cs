// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Hosting;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Provides a mechanism for components in the inner (script) host to observe and trigger
    /// lifecycle events of the outer (web) host application.
    /// </summary>
    public interface IScriptApplicationLifetime : IHostApplicationLifetime
    {
    }
}
