// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Features
{
    public interface IFunctionExecutionFeature
    {
        bool CanExecute { get; }

        FunctionDescriptor Descriptor { get; }

        Task ExecuteAsync(HttpRequest request, CancellationToken cancellationToken);
    }
}
