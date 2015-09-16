using System;
// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.ObjectModel;

namespace Microsoft.Azure.WebJobs.Script
{
    public class FunctionDescriptor
    {
        public string Name { get; set; }
        public Type ReturnType { get; set; }
        public Collection<ParameterDescriptor> Parameters { get; set; }
        public IFunctionInvoker Invoker { get; set; }
    }
}
