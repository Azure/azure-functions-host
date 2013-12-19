using System.Collections.Generic;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class AssemblyNameComparer : IEqualityComparer<AssemblyName>
    {
        public bool Equals(AssemblyName x, AssemblyName y)
        {
            return x.ToString() == y.ToString();
        }

        public int GetHashCode(AssemblyName obj)
        {
            return obj.ToString().GetHashCode();
        }
    }
}
