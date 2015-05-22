// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// Class containing helper methods for path binding
    /// </summary>
    public static class BindingDataPathHelper
    {
        /// <summary>
        /// Converts all parameter values in the specified binding data to their path compatible string values.
        /// </summary>
        /// <param name="bindingData">The binding data to convert.</param>
        /// <returns>A collection of path compatible parameters.</returns>
        public static IReadOnlyDictionary<string, string> ConvertParameters(IReadOnlyDictionary<string, object> bindingData)
        {
            if (bindingData == null)
            {
                throw new ArgumentNullException("bindingData");
            }

            Dictionary<string, string> parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (bindingData != null)
            {
                foreach (KeyValuePair<string, object> item in bindingData)
                {
                    string stringParamValue = ConvertParameterValueToString(item.Value);

                    if (stringParamValue != null)
                    {
                        parameters.Add(item.Key, stringParamValue);
                    }
                }
            }

            return parameters;
        }

        /// <summary>
        /// Convert a parameter value of supported type into path compatible string value.
        /// The set of supported types is limited to built-in signed/unsigned integer types, 
        /// strings, and Guid (which is translated in canonical form without curly braces).
        /// </summary>
        /// <param name="parameterValue">The parameter value to convert</param>
        /// <returns>Path compatible string representation of the given parameter or null if its type is not supported.</returns>
        public static string ConvertParameterValueToString(object parameterValue)
        {
            // TODO: Consider unifying with TToStringConverterFactory, though that selects a fixed
            // converter at indexing time and this waits until invocation time to decide how to convert.
            if (parameterValue != null)
            {
                switch (Type.GetTypeCode(parameterValue.GetType()))
                {
                    case TypeCode.String:
                        return (string)parameterValue;
                    case TypeCode.Int16:
                        return ((Int16)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.Int32:
                        return ((Int32)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.Int64:
                        return ((Int64)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.UInt16:
                        return ((UInt16)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.UInt32:
                        return ((UInt32)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.UInt64:
                        return ((UInt64)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.Char:
                        return ((Char)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.Byte:
                        return ((Byte)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.SByte:
                        return ((SByte)parameterValue).ToString(CultureInfo.InvariantCulture);
                    case TypeCode.Object:
                        if (parameterValue is Guid)
                        {
                            return parameterValue.ToString();
                        }
                        return null;
                }
            }

            return null;
        }
    }
}
