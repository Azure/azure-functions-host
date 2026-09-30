// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware;

/// <summary>
/// Identifies requests that <see cref="PlaceholderSpecializationMiddleware"/> lets through while specialization is
/// still in progress, instead of holding them until it completes.
/// </summary>
/// <remarks>
/// Register an implementation only for a request that specialization itself depends on, where holding the request
/// would prevent specialization from completing. The handler for a bypassed request runs before the host has
/// specialized and must tolerate that state.
/// </remarks>
/// <example>
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IPlaceholderSpecializationBypass, MyBypass&gt;());
/// </code>
/// </example>
public interface IPlaceholderSpecializationBypass
{
    /// <summary>
    /// Determines whether the request continues without waiting for specialization to complete.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> to let the request through while specialization is in progress.</returns>
    bool ShouldBypass(HttpRequest request);
}
