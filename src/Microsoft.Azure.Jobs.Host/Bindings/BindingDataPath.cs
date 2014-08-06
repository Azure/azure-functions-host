// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Azure.Jobs.Host.Bindings
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
            while (true)
            {
                if ((iActual == blobActual.Length) && (iPattern == blobPattern.Length))
                {
                    // Success
                    return namedParams;
                }
                if ((iActual == blobActual.Length) || (iPattern == blobPattern.Length))
                {
                    // Finished at different times. Mismatched
                    return null;
                }

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

            throw new NotImplementedException();
        }

        public static IReadOnlyDictionary<string, string> GetParameters(IReadOnlyDictionary<string, object> bindingData)
        {
            Dictionary<string, string> parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (bindingData != null)
            {
                foreach (KeyValuePair<string, object> item in bindingData)
                {
                    string value = item.Value as string;

                    if (value != null)
                    {
                        parameters.Add(item.Key, value);
                    }
                }
            }

            return parameters;
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
