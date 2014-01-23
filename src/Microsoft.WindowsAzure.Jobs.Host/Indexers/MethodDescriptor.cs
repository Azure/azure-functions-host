using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Abstraction over a MethodInfo so that we can bind from either
    // attributes or code-config.
    internal class MethodDescriptor
    {
        public string Name;
        public Attribute[] MethodAttributes;
        public ParameterInfo[] Parameters;
    }
}
