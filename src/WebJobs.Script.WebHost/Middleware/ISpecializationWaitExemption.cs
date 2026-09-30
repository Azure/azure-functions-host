// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware;

/// <summary>
/// Identifies requests that <see cref="PlaceholderSpecializationMiddleware"/> does not hold while placeholder
/// specialization is in progress.
/// </summary>
/// <remarks>
/// Register an implementation only for a request that specialization itself depends on, where holding the request
/// would prevent specialization from completing. The handler for an exempt request runs before the host has
/// specialized and must tolerate that state. The standard host registers none; the compute (worker-separated) host
/// exempts worker link requests.
/// </remarks>
/// <example>
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;ISpecializationWaitExemption, MyExemption&gt;());
/// </code>
/// </example>
public interface ISpecializationWaitExemption
{
    /// <summary>
    /// Determines whether the request continues without waiting for specialization to complete.
    /// </summary>
    /// <param name="request">The incoming request.</param>
    /// <returns><see langword="true"/> when the request is exempt from waiting for specialization.</returns>
    bool IsExempt(HttpRequest request);
}
