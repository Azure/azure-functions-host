// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IScriptJobHost : IJobHost
    {
        ICollection<FunctionDescriptor> Functions { get; }

        IDictionary<string, ICollection<string>> FunctionErrors { get; }
    }
}