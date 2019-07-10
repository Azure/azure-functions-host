// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class SystemAssemblyManager : ISystemAssemblyManager
    {
        private readonly ConcurrentDictionary<Assembly, bool> _systemAssemblies = new ConcurrentDictionary<Assembly, bool>();
        private static readonly IEnumerable<AssemblySearchPattern> _systemAssemblySearchPatterns = GetSystemAssemblySearchPatterns();

        public bool IsSystemAssembly(Assembly assembly)
        {
            return _systemAssemblies.GetOrAdd(assembly, a => _systemAssemblySearchPatterns.Any(p => p.IsMatch(a)));
        }

        private static IEnumerable<AssemblySearchPattern> GetSystemAssemblySearchPatterns()
        {
            return new[]
            {
                AssemblySearchPattern.ExactMatch("Microsoft.Azure.WebJobs.Script"),
                AssemblySearchPattern.StartsWith("Microsoft.Azure.WebJobs", "31bf3856ad364e35"),
                AssemblySearchPattern.StartsWith("Microsoft.Azure.Functions.Extensions", "f655f4c90a0eae19")
            };
        }

        private class AssemblySearchPattern
        {
            private readonly MatchType _matchType;

            private AssemblySearchPattern(MatchType matchType, string pattern, bool allowUnsigned, params string[] publicKeyTokens)
            {
                _matchType = matchType;
                Pattern = pattern;
                AllowUnsigned = allowUnsigned;
                PublicKeyTokens = publicKeyTokens ?? Enumerable.Empty<string>();
            }

            private enum MatchType
            {
                Exact,
                StartsWith
            }

            public bool AllowUnsigned { get; private set; }

            public string Pattern { get; private set; }

            public IEnumerable<string> PublicKeyTokens { get; private set; }

            public static AssemblySearchPattern ExactMatch(string pattern, params string[] publicKeyTokens)
            {
                return new AssemblySearchPattern(MatchType.Exact, pattern, false, publicKeyTokens);
            }

            public static AssemblySearchPattern ExactMatch(string pattern)
            {
                return new AssemblySearchPattern(MatchType.Exact, pattern, true, null);
            }

            public static AssemblySearchPattern StartsWith(string pattern, params string[] publicKeyTokens)
            {
                return new AssemblySearchPattern(MatchType.StartsWith, pattern, false, publicKeyTokens);
            }

            public bool IsMatch(Assembly assembly)
            {
                if (assembly == typeof(SystemAssemblyManager).Assembly)
                {
                    return true;
                }

                AssemblyName name = assembly.GetName();

                switch (_matchType)
                {
                    case MatchType.Exact:
                        if (string.Compare(Pattern, name.Name) != 0)
                        {
                            return false;
                        }
                        break;
                    case MatchType.StartsWith:
                        if (!name.Name.StartsWith(Pattern))
                        {
                            return false;
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown match type: '{_matchType}'.");
                }

                byte[] publicKey = name.GetPublicKeyToken();
                if (publicKey.Length == 0)
                {
                    return AllowUnsigned;
                }

                return PublicKeyTokens.Contains(ConvertKeyToString(publicKey));
            }

            private string ConvertKeyToString(byte[] bytes)
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes)
                {
                    sb.AppendFormat("{0:x2}", b);
                }
                return sb.ToString();
            }
        }
    }
}
