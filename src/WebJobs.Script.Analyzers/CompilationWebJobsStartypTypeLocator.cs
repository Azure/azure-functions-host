// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Azure.WebJobs.Hosting;

namespace Microsoft.Azure.Functions.Analyzers
{
    public class CompilationWebJobsStartupTypeLocator : IWebJobsStartupTypeLocator
    {
        private readonly Assembly[] _assemblies;
        private readonly Lazy<Type[]> _startupTypes;

        public CompilationWebJobsStartupTypeLocator()
        {
            _startupTypes = new Lazy<Type[]>(GetTypes);
        }

        internal CompilationWebJobsStartupTypeLocator(Assembly[] assemblies)
            : this()
        {
            _assemblies = assemblies;
        }

        public Type[] GetStartupTypes()
        {
            var types = _startupTypes.Value;
            return _startupTypes.Value;
        }

        public Type[] GetTypes()
        {
            return _assemblies?.SelectMany(a => a.GetCustomAttributes<WebJobsStartupAttribute>().Select(a => a.WebJobsStartupType)).ToArray();
        }
    }
}