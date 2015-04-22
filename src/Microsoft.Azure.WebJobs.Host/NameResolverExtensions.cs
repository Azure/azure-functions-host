// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>Contains extension methods for INameResolver.</summary>
    internal static class NameResolverExtensions
    {
        /// <summary>
        /// Resolve all %% matches within a string.
        /// </summary>
        /// <param name="resolver">resolver to apply to each name</param>
        /// <param name="wholeString">the input string. IE, "start%name1%...%name2%end"</param>
        /// <returns>The resolved string. IE, "startA...Bend" </returns>
        public static string ResolveWholeString(this INameResolver resolver, string wholeString)
        {
            if (resolver == null)
            {
                throw new ArgumentNullException("resolver");
            }

            if (wholeString == null)
            {
                return null;
            }

            int i = 0;
            StringBuilder sb = new StringBuilder();

            while (i < wholeString.Length)
            {
                int idxStart = wholeString.IndexOf('%', i);
                if (idxStart >= 0)
                {
                    int idxEnd = wholeString.IndexOf('%', idxStart + 1);
                    if (idxEnd < 0)
                    {
                        string msg = string.Format("The '%' at position {0} does not have a closing '%'", idxStart);
                        throw new InvalidOperationException(msg);
                    }
                    string name = wholeString.Substring(idxStart + 1, idxEnd - idxStart - 1);

                    string value;
                    try
                    {
                        value = resolver.Resolve(name);
                    }
                    catch (Exception e)
                    {
                        string msg = string.Format("Threw an exception trying to resolve '%{0}%' ({1}:{2}).", name, e.GetType().Name, e.Message);
                        throw new InvalidOperationException(msg, e);
                    }
                    if (value == null)
                    {
                        string msg = string.Format("'%{0}%' does not resolve to a value.", name);
                        throw new InvalidOperationException(msg);
                    }
                    sb.Append(wholeString.Substring(i, idxStart - i));
                    sb.Append(value);
                    i = idxEnd + 1;
                }
                else
                {
                    // no more '%' tokens
                    sb.Append(wholeString.Substring(i));
                    break;
                }
            }

            return sb.ToString();
        }
    }
}
