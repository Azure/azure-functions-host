// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    /// <summary>
    /// Represents a NuGet package reference.
    /// </summary>
    public class PackageReference
    {
        public PackageReference(string name, string version)
        {
            Name = name;
            Version = version;
            Assemblies = new Dictionary<AssemblyName, string>(new AssemblyNameEqualityComparer());
            FrameworkAssemblies = new Dictionary<AssemblyName, string>(new AssemblyNameEqualityComparer());
        }

        public Dictionary<AssemblyName, string> Assemblies { get; private set; }

        public Dictionary<AssemblyName, string> FrameworkAssemblies { get; private set; }

        public string Name { get; private set; }

        public string Version { get; private set; }

        private class AssemblyNameEqualityComparer : IEqualityComparer<AssemblyName>
        {
            public bool Equals(AssemblyName x, AssemblyName y)
            {
                if (x == y)
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return string.Compare(x.FullName, y.FullName, StringComparison.OrdinalIgnoreCase) == 0;
            }

            public int GetHashCode(AssemblyName obj)
            {
                if (obj == null || obj.FullName == null)
                {
                    return 0;
                }

                return obj.FullName.GetHashCode();
            }
        }
    }
}
