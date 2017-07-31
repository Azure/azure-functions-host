// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.WebJobs.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    // Scan over user types with [Binding] attributes and extract the set of assemblies.
    internal class UserTypeScanner
    {
        // Scan user types for potential binding extensions.
        // Return mapping of new assemblies along with a hint path for what triggered each.
        public static Dictionary<Assembly, string> GetPossibleExtensionAssemblies(IEnumerable<Type> userTypes)
        {
            // List of possible extension assemblies.
            var possibleExtensionAssemblies = new Dictionary<Assembly, string>();

            foreach (var type in userTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    ScanMethod(possibleExtensionAssemblies, method);
                }
            }
            return possibleExtensionAssemblies;
        }

        private static void ScanMethod(Dictionary<Assembly, string> possibleExtensionAssemblies, MethodInfo method)
        {
            foreach (var parameter in method.GetParameters())
            {
                ScanParameter(possibleExtensionAssemblies, parameter);
            }

            ScanParameter(possibleExtensionAssemblies, method.ReturnParameter);
        }

        private static void ScanParameter(Dictionary<Assembly, string> possibleExtensionAssemblies, ParameterInfo parameter)
        {
            foreach (var attr in parameter.GetCustomAttributes())
            {
                if (IsBindingAttribute(attr))
                {
                    Assembly assembly = attr.GetType().Assembly;

                    var method = parameter.Member;
                    string locationHint = $"referenced by: Method='{method.DeclaringType.FullName}.{method.Name}', Parameter='{parameter.Name}'.";

                    possibleExtensionAssemblies[assembly] = locationHint;
                }
            }
        }

        private static bool IsBindingAttribute(Attribute attribute)
        {
            var bindingAttr = attribute.GetType().GetCustomAttribute<BindingAttribute>();
            return bindingAttr != null;
        }
    }
}
