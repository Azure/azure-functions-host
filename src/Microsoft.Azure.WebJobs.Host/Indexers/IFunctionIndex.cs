// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host.Protocols;

namespace Microsoft.Azure.WebJobs.Host.Indexers
{
    internal interface IFunctionIndex : IFunctionIndexLookup
    {
        void Add(IFunctionDefinition function, FunctionDescriptor descriptor, MethodInfo method);

        IEnumerable<IFunctionDefinition> ReadAll();

        IEnumerable<FunctionDescriptor> ReadAllDescriptors();

        IEnumerable<MethodInfo> ReadAllMethods();
    }
}
