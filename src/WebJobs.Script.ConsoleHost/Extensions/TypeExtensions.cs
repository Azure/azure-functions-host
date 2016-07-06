// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WebJobs.Script.ConsoleHost.Extensions
{
    public static class TypeExtensions
    {
        public static IEnumerable<Type> GetImplementingTypes(this Type baseType)
        {
            return Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);
        }
    }
}
