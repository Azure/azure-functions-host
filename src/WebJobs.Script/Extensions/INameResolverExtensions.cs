// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using Microsoft.Azure.WebJobs.Host;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class INameResolverExtensions
    {
        // Apply the resolver to all string properties on obj that have an [AllowNameResolution] attribute.
        // Updates the object in-place. 
        public static void ResolveAllProperties(this INameResolver resolver, object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException("value");
            }
            foreach (var prop in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType == typeof(string))
                {
                    if (prop.GetCustomAttribute<AutoResolveAttribute>() != null)
                    {
                        string val = (string)prop.GetValue(value);
                        string newVal = resolver.ResolveWholeString(val);
                        prop.SetValue(value, newVal);
                    }
                }
            }
        }
    }
}