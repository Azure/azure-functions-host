using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Resolve %name% in the endpoints. 
    /// </summary>
    public interface INameResolver
    {
        /// <summary>
        /// Resolve a %name% to a value . Resolution is not recursive. 
        /// </summary>
        /// <param name="name">The name to resolve (without the %... %)</param>
        /// <returns>the value that the name resolves to. Or Null if the name is not supported.</returns>
        string Resolve(string name);
    }

    internal class DefaultNameResolver : INameResolver
    {
        public string Resolve(string name)
        {
            throw new NotImplementedException("INameResolver must be supplied to resolve '%" + name +"%'.");
        }
    }

    /// <summary>
    /// Extensions for INameResolver
    /// </summary>
    internal static class INameResolverExtension
    {
        /// <summary>
        /// Resolve all %% matches within a string. 
        /// </summary>
        /// <param name="resolver">resolver to apply to each name</param>
        /// <param name="wholeString">the input string. IE, "start%name1%...%name2%end"</param>
        /// <returns>The resolved string. IE, "startA...Bend" </returns>        
        public static string ResolveWholeString(this INameResolver resolver, string wholeString)
        {
            if (wholeString == null)
            {
                return null;
            }
            if (resolver == null)
            {
                return wholeString;
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
                        string msg = string.Format("'%{0}%' threw an exception trying to resolve ({1}:{2}).", name, e.GetType().Name, e.Message);
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