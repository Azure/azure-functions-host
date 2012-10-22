using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;

namespace Orchestrator
{
    // Describes how a function can get triggered.
    // This is orthogonal to the binding. 
    public class FunctionTrigger
    {
        // If != 0, then specify the function is invoked on the timer. 
        public string TimerInterval { get; set; }

        // ### Table storage chokes on TimeSpan,
        public TimeSpan? GetTimerInterval()
        {
            if (this.TimerInterval == null)
            {
                return null;
            }
            return TimeSpan.Parse(this.TimerInterval);
        }
                
        // True if invocation should use a blob listener.
        public bool ListenOnBlobs { get; set; }
    }

    // Persist a FunctionDescriptor in azure table storage.
    // Must be flat. 
    // $$$ This has extra metadata beyond just FunctionLocation (like Descr, Guid (rowkey), in/out, ... ) 
    // This should be static once a function is uploaded. It can obviously change when we refresh a function and load 
    // a new version. So the Timestamp property gives us "last modified time"
    // But it shouldn't change just be executing the function. Store that invocation information somewhere else.
    [DataServiceKey("PartitionKey", "RowKey")]    
    public class FunctionIndexEntity : TableServiceEntity
    {
        public FunctionIndexEntity()
        {
            this.PartitionKey = FunctionIndexEntity.PartionKey;            
            // Row key is based on function identity. That keeps us from indexing the same function multiple times.
        }

        // User description of the function
        public string Description { get; set; }

        // Where the function lives. Location is effectively the row key. 
        public FunctionLocation Location { get; set; }

        // What causes the function to get triggered. 
        // This is used by the orchestrator service.
        public FunctionTrigger Trigger { get; set; }

        // How to bind the parameters. 
        public FunctionFlow Flow { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return Utility.GetAccount(this.Location.Blob.AccountConnectionString);
        }
                
        public void SetRowKey()
        {
            this.RowKey = GetRowKey(this.Location);
        }

        // See illegal characters in row keys
        // http://msdn.microsoft.com/en-us/library/dd179338(v=MSDN.10).aspx
        public static string GetRowKey(FunctionLocation location)
        {
            return location.GetId().Replace('\\', '.');
        }
        public const string PartionKey = "1";

        public override string ToString()
        {
            return this.Location.ToString();
        }
    }
}