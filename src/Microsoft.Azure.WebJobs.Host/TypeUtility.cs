// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class TypeUtility
    {
        /// <summary>
        /// Walk from the parameter up to the containing type, looking for an instance
        /// of the specified attribute type, returning it if found.
        /// </summary>
        /// <param name="parameter">The parameter to check.</param>
        internal static T GetHierarchicalAttributeOrNull<T>(ParameterInfo parameter) where T : Attribute
        {
            if (parameter == null)
            {
                return null;
            }

            T attribute = parameter.GetCustomAttribute<T>();
            if (attribute != null)
            {
                return attribute;
            }

            return GetHierarchicalAttributeOrNull<T>((MethodInfo)parameter.Member);
        }

        /// <summary>
        /// Walk from the method up to the containing type, looking for an instance
        /// of the specified attribute type, returning it if found.
        /// </summary>
        /// <param name="method">The method to check.</param>
        internal static T GetHierarchicalAttributeOrNull<T>(MethodInfo method) where T : Attribute
        {
            T attribute = method.GetCustomAttribute<T>();
            if (attribute != null)
            {
                return attribute;
            }

            attribute = method.DeclaringType.GetCustomAttribute<T>();
            if (attribute != null)
            {
                return attribute;
            }

            return null;
        }
    }
}
