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
    public class ModelBinderManifest
    {
        // List of model binders. Invoke these to set the configuration
        public Entry[] Entries { get; set; }

        // Call this function in the given assembly, type:
        //   public static Initialize(IConfiguration) 
        public class Entry
        {
            public string AssemblyName { get; set; }
            public string TypeName { get; set; }
        }
    }
    
    // Result of Binder table. 
    // This is a table that maps types to model binders on the cloud. 
    // Like a cloud-based IOC. 
    public class BinderEntry
    {
        public string InitType { get; set; }
        public string InitAssembly { get; set; }
        public CloudBlobPath Path { get; set; }
    }
}