// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Defines a filter that will be called as part of the function invocation pipeline.
    /// </summary>
    public interface IFunctionInvocationFilter
    {
        /// <summary>
        /// Method invoked before the target function is called
        /// </summary>
        /// <param name="executingContext">The context</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns></returns>
        Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken);

        /// <summary>
        /// Method invoked after the target function is called
        /// </summary>
        /// <param name="executedContext">The context</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns></returns>
        Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken);
    }
}