// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Host
{
    internal static class TypeUtility
    {
        /// <summary>
        /// Given a type that could be bound to IAsyncCollector, extract the TMessage
        /// </summary>
        /// <remarks>
        /// For example, the core Type is T in the following parameters:
        /// <list type="bullet">
        /// <item><description><see cref="ICollector{T}"/></description></item>
        /// <item><description>T[]</description></item>
        /// <item><description>out T</description></item>
        /// <item><description>out T[]</description></item>
        /// </list>
        /// </remarks>
        /// <param name="type">The Type to evaluate.</param>
        /// <returns>The core Type</returns>
        public static Type GetMessageTypeFromAsyncCollector(Type type)
        {
            Type coreType = type;
            if (coreType.IsByRef)
            {
                coreType = coreType.GetElementType();

                // We 'unwrap' Arrays to support 
                // 'out POCO[]' parameters. All other
                // out parameters we return as-is.
                if (coreType.IsArray)
                {
                    coreType = coreType.GetElementType();
                }

                return coreType;
            }

            if (coreType.IsGenericType)
            {
                Type genericArgType = null;
                if (TryGetSingleGenericArgument(coreType, out genericArgType))
                {
                    return genericArgType;
                }

                throw new InvalidOperationException("Binding parameter types can only have one generic argument.");
            }

            return coreType;
        }

        /// <summary>
        /// Checks whether the specified type has a single generic argument. If so,
        /// that argument is returned via the out parameter.
        /// </summary>
        /// <param name="genericType">The generic type.</param>
        /// <param name="genericArgumentType">The single generic argument.</param>
        /// <returns>true if there was a single generic argument. Otherwise, false.</returns>
        public static bool TryGetSingleGenericArgument(Type genericType, out Type genericArgumentType)
        {
            genericArgumentType = null;
            Type[] genericArgTypes = genericType.GetGenericArguments();

            if (genericArgTypes.Length != 1)
            {
                return false;
            }

            genericArgumentType = genericArgTypes[0];
            return true;
        }

        internal static bool IsValidOutType(Type paramType, Func<Type, bool> verifyCoreType)
        {
            if (paramType.IsByRef)
            {
                Type coreType = paramType.GetElementType();
                if (coreType.IsArray)
                {
                    coreType = coreType.GetElementType();
                }

                return verifyCoreType(coreType);
            }

            return false;
        }

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
