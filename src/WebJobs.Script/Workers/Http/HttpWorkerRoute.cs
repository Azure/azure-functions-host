// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Microsoft.Azure.WebJobs.Script.Workers.Http
{
    /// <summary>
    /// Route mapping for a custom handler HTTP worker.
    /// </summary>
    /// <param name="Route">Route template (e.g. "/my/route", "{*route}").</param>
    /// <param name="AuthorizationLevel">Authorization level (default Function).</param>
    public record HttpWorkerRoute(string Route, AuthorizationLevel AuthorizationLevel = AuthorizationLevel.Function);
}
