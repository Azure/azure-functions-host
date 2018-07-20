// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Reflection;
using System.Runtime.Loader;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    public interface IScriptBindingProvider
    {
        bool TryCreate(ScriptBindingContext context, out ScriptBinding binding);

        bool TryResolveAssembly(string assemblyName, AssemblyLoadContext targetContext, out Assembly assembly);
    }
}