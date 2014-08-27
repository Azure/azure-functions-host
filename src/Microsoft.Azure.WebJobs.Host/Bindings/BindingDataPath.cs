// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    internal static class BindingDataPath
    {
        // Given a path, collect all the keys in the path.
        // Eg "{name},{date}" would collect "name" and "date".
        public static void AddParameterNames(string path, ICollection<string> parameterNames)
        {
            int index = 0;
            while (index < path.Length)
            {
                char character = path[index];
                if (character == '{')
                {
                    // Find closing
                    int endIndex = path.IndexOf('}', index);
                    if (endIndex == -1)
                    {
                        throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                    }
                    string name = path.Substring(index + 1, endIndex - index - 1);
                    parameterNames.Add(name);
                    index = endIndex + 1;
                }
                else
                {
                    index++;
                }
            }
        }

        // If blob names matches actual pattern; null if no match.
        public static IReadOnlyDictionary<string, object> CreateBindingData(string pattern, string actualPath)
        {
            string containerPattern;
            string blobPattern;

            string containerActual;
            string blobActual;

            SplitBlobPath(pattern, out containerPattern, out blobPattern);
            SplitBlobPath(actualPath, out containerActual, out blobActual);

            // Containers must match
            if (!String.Equals(containerPattern, containerActual, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Pattern is container only. 
            if (blobPattern == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); // empty dict, no blob parameters                    
            }

            Dictionary<string, object> namedParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            int iPattern = blobPattern.Length - 1;
            int iActual = blobActual.Length - 1;
            while (iActual >= 0 && iPattern >= 0)
            {
                char ch = blobPattern[iPattern];
                if (ch == '}')
                {
                    // End of a named parameter. 
                    int iStart = blobPattern.LastIndexOf('{', iPattern);
                    if (iStart == -1)
                    {
                        throw new InvalidOperationException("Missing Opening bracket");
                    }
                    string name = blobPattern.Substring(iStart + 1, iPattern - iStart - 1);

                    if (iStart == 0)
                    {
                        // '{' was the last character. Match to end of string
                        string valueRestOfLine = blobActual.Substring(0, iActual + 1);
                        namedParams[name] = valueRestOfLine;
                        return namedParams; // Success
                    }

                    char startingCh = blobPattern[iStart - 1];

                    // Scan actual
                    int iActualStart = blobActual.LastIndexOf(startingCh, iActual);
                    if (iActualStart == -1)
                    {
                        // Don't match
                        return null;
                    }
                    string value = blobActual.Substring(iActualStart + 1, iActual - iActualStart);
                    namedParams[name] = value;

                    iPattern = iStart - 1; // -1 to move before }
                    iActual = iActualStart;
                }
                else
                {
                    if (ch == blobActual[iActual])
                    {
                        // Match
                        iActual--;
                        iPattern--;
                        continue;
                    }
                    else
                    {
                        // Don't match
                        return null;
                    }
                }
            }

            if (iActual == iPattern)
            {
                // Success
                return namedParams;
            }

            // Finished at different times. Mismatched
            return null;
        }

        public static IReadOnlyDictionary<string, string> GetParameters(IReadOnlyDictionary<string, object> bindingData)
        {
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
        /// List of supported types is limited to built-in signed/unsigned integer types, 
        /// strings, and Guid. The latter one is translated in canonical form without curly braces.
        /// </summary>
        /// <param name="parameterValue">Any parameter value</param>
        /// <returns>Path compatible string representation of the given parameter or null if its type is not supported.</returns>
        public static string ConvertParameterValueToString(object parameterValue)
        {
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

        public static string Resolve(string path, IReadOnlyDictionary<string, string> parameters)
        {
            StringBuilder builder = new StringBuilder();
            int index = 0;
            while (index < path.Length)
            {
                char character = path[index];
                if (character == '{')
                {
                    // Find closing
                    int endIndex = path.IndexOf('}', index);
                    if (endIndex == -1)
                    {
                        throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                    }
                    string name = path.Substring(index + 1, endIndex - index - 1);
                    string value = null;
                    if (parameters != null)
                    {
                        parameters.TryGetValue(name, out value);
                    }
                    if (value == null)
                    {
                        throw new InvalidOperationException(String.Format("No value for name parameter '{0}'", name));
                    }
                    else
                    {
                        builder.Append(value);
                    }
                    index = endIndex + 1;
                }
                else
                {
                    builder.Append(character);
                    index++;
                }
            }
            return builder.ToString();
        }

        private static void SplitBlobPath(string input, out string container, out string blob)
        {
            Debug.Assert(input != null);

            var parts = input.Split(new[] { '/' }, 2);
            if (parts.Length == 1)
            {
                // No blob name
                container = input;
                blob = null;
                return;
            }

            container = parts[0];
            blob = parts[1];
        }
    }
}
