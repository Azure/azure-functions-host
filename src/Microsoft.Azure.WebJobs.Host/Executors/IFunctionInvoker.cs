// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    internal interface IFunctionInvoker
    {
        IReadOnlyList<string> ParameterNames { get; }

        // The cancellation token, if any, is provided along with the other arguments.
        Task InvokeAsync(object[] arguments);
    }
}
