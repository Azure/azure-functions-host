using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using SimpleBatch;

namespace RunnerInterfaces
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
                return (string.Compare(this.AssemblyName, x.AssemblyName, true) == 0) &&
                       (string.Compare(this.TypeName, x.TypeName, true) == 0);
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

    // Result of Binder table. 
    // The partition/row key for the table specifies the Type that this binder applies to.
    // This BinderEntry then specifies where to find the binder.  
    // This is a table that maps types to model binders on the cloud. 
    // Like a cloud-based IOC. 
    internal class BinderEntry
    {
        public string AccountConnectionString { get; set; }
        public string InitType { get; set; }
        public string InitAssembly { get; set; }
        public CloudBlobPath Path { get; set; }
    }
}