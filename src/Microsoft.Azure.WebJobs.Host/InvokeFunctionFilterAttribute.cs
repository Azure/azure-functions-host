// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// An invocation filter that invokes job methods.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
    public sealed class InvokeFunctionFilterAttribute : InvocationFilterAttribute
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="executingFilter">The name of the function to execute before the target function is called.</param>
        /// <param name="executedFilter">The name of the function to execute after the target function is called.</param>
        public InvokeFunctionFilterAttribute(string executingFilter = null, string executedFilter = null)
        {
            ExecutingFilter = executingFilter;
            ExecutedFilter = executedFilter;
        }

        /// <summary>
        /// The name of the function to execute before the target function is called.
        /// </summary>
        public string ExecutingFilter { get; }

        /// <summary>
        /// The name of the function to execute after the target function is called.
        /// </summary>
        public string ExecutedFilter { get; }

        /// <inheritdoc/>
        public override async Task OnExecutingAsync(FunctionExecutingContext executingContext, CancellationToken cancellationToken)
        {
            if (executingContext == null)
            {
                throw new ArgumentNullException(nameof(executingContext));
            }

            if (!string.IsNullOrEmpty(ExecutingFilter))
            {
                executingContext.Logger?.LogInformation($"Executing Function Filter '{ExecutingFilter}'");

                await InvokeJobFunctionAsync(ExecutingFilter, executingContext, cancellationToken);
            }
        }

        /// <inheritdoc/>
        public override async Task OnExecutedAsync(FunctionExecutedContext executedContext, CancellationToken cancellationToken)
        {
            if (executedContext == null)
            {
                throw new ArgumentNullException(nameof(executedContext));
            }

            if (!string.IsNullOrEmpty(ExecutedFilter))
            {
                executedContext.Logger?.LogInformation($"Executing Function Filter '{ExecutedFilter}'");

                await InvokeJobFunctionAsync(ExecutedFilter, executedContext, cancellationToken);
            }
        }

        internal static async Task InvokeJobFunctionAsync<TContext>(string methodName, TContext context, CancellationToken cancellationToken) where TContext : FunctionInvocationContext
        {
            // See <see cref="Microsoft.Azure.WebJobs.Host.FunctionFilterBindingProvider"/> which
            // is responsible for binding to this context.
            // The binding is by Type, so the name we use here doesn't matter.
            var invokeArguments = new Dictionary<string, object>()
            {
                { "$invocationContext", context }
            };

            await context.Invoker.CallAsync(methodName, invokeArguments, cancellationToken);
        }
    }
}