// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class BoolUtility
    {
        public static bool TryReadAsBool(IDictionary<string, object> properties, string propertyKey, out bool result)
        {
            if (properties.TryGetValue(propertyKey, out object valueObject))
            {
                if (valueObject is bool boolValue)
                {
                    return result = boolValue;
                }
                else if (valueObject is string stringValue)
                {
                    return bool.TryParse(stringValue, out result);
                }
            }

            return result = false;
        }
    }
}