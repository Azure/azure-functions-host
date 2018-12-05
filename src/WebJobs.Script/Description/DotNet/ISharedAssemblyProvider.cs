// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    internal interface ISharedAssemblyProvider
    {
        bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, ILogger logger, out Assembly assembly);
    }
}
