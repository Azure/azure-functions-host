// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public interface IFunctionInvoker
    {
        /// <summary>
        /// Gets logging information for this function.
        /// </summary>
        FunctionLogger LogInfo { get; }

        /// <summary>
        /// Invoke the function using the specified parameters.
        /// </summary>
        /// <param name="parameters">The parameters.</param>
        /// <returns>A <see cref="Task"/> for the invocation.</returns>
        Task<object> Invoke(object[] parameters);

        /// <summary>
        /// This method is called by the host when invocation exceptions occur
        /// outside of the invocation. This allows the invoker to inspect/log the
        /// exception as necessary.
        /// </summary>
        /// <param name="ex">The <see cref="Exception"/> that occurred.</param>
        void OnError(Exception ex);
    }
}
