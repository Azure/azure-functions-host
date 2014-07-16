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
            string container1;
            string blob1;

            string container2;
            string blob2;

            SplitBlobPath(pattern, out container1, out blob1); // may just bec container
            SplitBlobPath(actualPath, out container2, out blob2); // should always be full

            // Containers must match
            if (!String.Equals(container1, container2, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Pattern is container only. 
            if (blob1 == null)
            {
                return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); // empty dict, no blob parameters                    
            }

            // Special case for extensions 
            // Let "{name}.csv" match against "a.b.csv", where name = "a.b"
            // $$$ This is getting close to a regular expression...
            {
                if (pattern.Length > 4 && actualPath.Length > 4)
                {
                    string ext = pattern.Substring(pattern.Length - 4);
                    if (ext[0] == '.')
                    {
                        if (actualPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                        {
                            pattern = pattern.Substring(0, pattern.Length - 4);
                            actualPath = actualPath.Substring(0, actualPath.Length - 4);

                            return CreateBindingData(pattern, actualPath);
                        }
                    }
                }
            }

            // Now see if the actual input matches against the pattern

            Dictionary<string, object> namedParams = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            int iPattern = 0;
            int iActual = 0;
            while (true)
            {
                if ((iActual == blob2.Length) && (iPattern == blob1.Length))
                {
                    // Success
                    return namedParams;
                }
                if ((iActual == blob2.Length) || (iPattern == blob1.Length))
                {
                    // Finished at different times. Mismatched
                    return null;
                }


                char ch = blob1[iPattern];
                if (ch == '{')
                {
                    // Start of a named parameter. 
                    int iEnd = blob1.IndexOf('}', iPattern);
                    if (iEnd == -1)
                    {
                        throw new InvalidOperationException("Missing closing bracket");
                    }
                    string name = blob1.Substring(iPattern + 1, iEnd - iPattern - 1);

                    if (iEnd + 1 == blob1.Length)
                    {
                        // '}' was the last character. Match to end of string
                        string valueRestOfLine = blob2.Substring(iActual);
                        namedParams[name] = valueRestOfLine;
                        return namedParams; // Success
                    }
                    char closingCh = blob1[iEnd + 1];

                    // Scan actual 
                    int iActualEnd = blob2.IndexOf(closingCh, iActual);
                    if (iActualEnd == -1)
                    {
                        // Don't match
                        return null;
                    }
                    string value = blob2.Substring(iActual, iActualEnd - iActual);
                    namedParams[name] = value;

                    iPattern = iEnd + 1; // +1 to move past }
                    iActual = iActualEnd;
                }
                else
                {
                    if (ch == blob2[iActual])
                    {
                        // Match
                        iActual++;
                        iPattern++;
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
