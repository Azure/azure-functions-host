// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.ExtensionsMetadataGenerator
{
    public static class TypeExtensions
    {
        private const string ExtensionInterfaceType = "Microsoft.Azure.WebJobs.Host.Config.IExtensionConfigProvider";

        public static bool IsExtensionType(this Type type)
        {
            if (!type.IsClass)
            {
                return false;
            }

            return type.GetInterfaces()
                .Any(t => string.Equals(t.FullName, ExtensionInterfaceType, StringComparison.OrdinalIgnoreCase));
        }
    }
}
