using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Microsoft.WindowsAzure.Jobs
{
    // Function is already loaded into memory. 
    internal class MethodInfoFunctionLocation : FunctionLocation
    {
        // $$$ Can't serialize this because it has a real methodinfo. We shouldn't be serializing these anyways. 
        // instead:
        // 1. caller should be converting location into a serializable type and running o
        // 2. entire scenario is already in-memory. 
        [JsonIgnore]
        public MethodInfo MethodInfo { get; set; }

        public string ShortName { get; set; }
        public string Id { get; set; }

        // For deserialization
        public string AssemblyQualifiedTypeName { get; set; }
        public string MethodName { get; set; }

        public MethodInfoFunctionLocation() // For Serialization
        {
        }

        public MethodInfoFunctionLocation(MethodInfo method)
        {
            MethodInfo = method;
            ShortName = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", MethodInfo.DeclaringType.Name, MethodInfo.Name);
            Id = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", MethodInfo.DeclaringType.FullName, MethodInfo.Name);
            FullName = Id;

            this.AssemblyQualifiedTypeName = MethodInfo.DeclaringType.AssemblyQualifiedName;
            this.MethodName = MethodInfo.Name;
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext ctx)
        {
            if (this.MethodInfo != null)
            {
                return;
            }

            if (AssemblyQualifiedTypeName != null && MethodName != null)
            {
                Type t = Type.GetType(AssemblyQualifiedTypeName);
                if (t != null)
                {
                    MethodInfo m = t.GetMethod(MethodName, BindingFlags.Public | BindingFlags.Static);

                    this.MethodInfo = m;
                }
            }
        }

        public override string GetShortName()
        {
            return ShortName;
        }

        public override string GetId()
        {
            return Id;
        }
    }
}
