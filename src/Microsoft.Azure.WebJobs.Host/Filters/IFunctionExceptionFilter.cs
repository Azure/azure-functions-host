// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Defines a filter that will be called as part of the function invocation pipeline
    /// for failed function invocations.
    /// </summary>
    public interface IFunctionExceptionFilter : IFunctionFilter
    {
        /// <summary>
        /// Handles the function exception.
        /// </summary>
        /// <param name="exceptionContext">The <see cref="FunctionExceptionContext"/> for the failed invocation.</param>
        /// <param name="cancellationToken">the cancellation token.</param>
        /// <returns></returns>
        Task OnExceptionAsync(FunctionExceptionContext exceptionContext, CancellationToken cancellationToken);
    }
}