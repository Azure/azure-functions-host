// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class BoolUtility
    {
        public static bool TryReadAsBool(IDictionary<string, object> properties, string propertyKey)
        {
            if (properties.TryGetValue(propertyKey, out object valObj))
            {
                if (valObj is bool)
                {
                    return (bool)valObj;
                }
                else
                {
                    return bool.TryParse(valObj as string, out bool valBool) ? valBool : false;
                }
            }

            return false;
        }
    }
}