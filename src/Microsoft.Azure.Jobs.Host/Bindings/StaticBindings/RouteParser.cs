using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.Jobs
{
    internal static class RouteParser
    {
        // Given "daas-test-input/{name}.csv" and a dict {name:bob}, 
        // returns: "daas-test-input/bob.csv"
        public static string ApplyNames(string pattern, IDictionary<string, string> nameParameters)
        {
            return ApplyNamesWorker(pattern, nameParameters, allowUnbound: false);
        }

        private static string ApplyNamesWorker(string pattern, IDictionary<string, string> names, bool allowUnbound)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < pattern.Length)
            {
                char ch = pattern[i];
                if (ch == '{')
                {
                    // Find closing
                    int iEnd = pattern.IndexOf('}', i);
                    if (iEnd == -1)
                    {
                        throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                    }
                    string name = pattern.Substring(i + 1, iEnd - i - 1);
                    string value = null;
                    if (names != null)
                    {
                        names.TryGetValue(name, out value);
                    }
                    if (value == null)
                    {
                        if (!allowUnbound)
                        {
                            throw new InvalidOperationException(String.Format("No value for name parameter '{0}'", name));
                        }
                        // preserve the unbound {name} pattern.
                        sb.Append('{');
                        sb.Append(name);
                        sb.Append('}');
                    }
                    else
                    {
                        sb.Append(value);
                    }
                    i = iEnd + 1;
                }
                else
                {
                    sb.Append(ch);
                    i++;
                }
            }
            return sb.ToString();
        }

        // Given a path, return all the keys in the path.
        // Eg "{name},{date}" would return "name" and "date".
        public static IEnumerable<string> GetParameterNames(string pattern)
        {
            List<string> names = new List<string>();

            int i = 0;
            while (i < pattern.Length)
            {
                char ch = pattern[i];
                if (ch == '{')
                {
                    // Find closing
                    int iEnd = pattern.IndexOf('}', i);
                    if (iEnd == -1)
                    {
                        throw new InvalidOperationException("Input pattern is not well formed. Missing a closing bracket");
                    }
                    string name = pattern.Substring(i + 1, iEnd - i - 1);
                    names.Add(name);
                    i = iEnd + 1;
                }
                else
                {
                    i++;
                }
            }
            return names;
        }

        public static bool HasParameterNames(string pattern)
        {
            return GetParameterNames(pattern).Any();
        }
    }
}
