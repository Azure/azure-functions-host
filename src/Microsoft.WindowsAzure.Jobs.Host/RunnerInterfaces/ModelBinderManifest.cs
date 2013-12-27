using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // Manifest of model binders that get dynamically invoked. 
    // This is created during indexing, and consumed by the RunnerHost.
    internal class ModelBinderManifest
    {
        // List of model binders. Invoke these to set the configuration
        public Entry[] Entries { get; set; }

        // Call this function in the given assembly, type:
        //   public static Initialize(IConfiguration) 
        internal class Entry
        {
            public string AssemblyName { get; set; }
            public string TypeName { get; set; }

            public override bool Equals(object obj)
            {
                Entry x = obj as Entry;
                if (x == null)
                {
                    return false;
                }
                return (String.Equals(AssemblyName, x.AssemblyName, StringComparison.OrdinalIgnoreCase)) &&
                       (String.Equals(TypeName, x.TypeName, StringComparison.OrdinalIgnoreCase));
            }
            public override int GetHashCode()
            {
                return this.AssemblyName.GetHashCode();
            }
            public override string ToString()
            {
                return this.TypeName;
            }
        }
    }
}
