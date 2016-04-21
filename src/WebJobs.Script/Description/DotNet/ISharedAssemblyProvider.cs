// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal interface ISharedAssemblyProvider
    {
        bool TryResolveAssembly(string assemblyName, out Assembly assembly);
    }
}
