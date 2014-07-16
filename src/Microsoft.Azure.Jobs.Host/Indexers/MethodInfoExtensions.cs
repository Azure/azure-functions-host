// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Indexers
{
    internal static class MethodInfoExtensions
    {
        public static string GetFullName(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException("methodInfo");
            }

            return String.Format(CultureInfo.InvariantCulture, "{0}.{1}", methodInfo.DeclaringType.FullName, methodInfo.Name);
        }

        public static string GetShortName(this MethodInfo methodInfo)
        {
            if (methodInfo == null)
            {
                throw new ArgumentNullException("methodInfo");
            }

            return String.Format(CultureInfo.InvariantCulture, "{0}.{1}", methodInfo.DeclaringType.Name, methodInfo.Name);
        }
    }
}
