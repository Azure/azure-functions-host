using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;

namespace RunnerInterfaces
{
    public class FunctionOutputLog
    {
        static Action empty = () => { };

        public FunctionOutputLog()
        {
            this.Output = Console.Out;
            this.CloseOutput = empty;
        }

        public TextWriter Output { get; set; }
        public Action CloseOutput { get; set; }
        public string Uri { get; set; } // Uri to refer to output 

        // Separate channel for logging structured (and updating) information about parameters
        public CloudBlobDescriptor ParameterLogBlob { get; set; }


        // Get a default instance of 
        public static FunctionOutputLog GetLogStream(FunctionInvokeRequest f, string accountConnectionString, string containerName)
        {            
            string name = f.ToString() + ".txt";

            var c = Utility.GetContainer(accountConnectionString, containerName);

            CloudBlob blob = c.GetBlobReference(name);            
            
            var period = TimeSpan.FromMinutes(1); // frequency to refresh
            var x = new BlobIncrementalTextWriter(blob, period);

            TextWriter tw = x.Writer;

            return new FunctionOutputLog
            {
                CloseOutput = () =>
                {
                    x.Close();
                },
                Uri = blob.Uri.ToString(),
                Output = tw,
                ParameterLogBlob = new CloudBlobDescriptor
                {
                     AccountConnectionString = accountConnectionString,
                     ContainerName = containerName,
                     BlobName = f.ToString() + ".params.txt"
                }
            };
        }
    }
}
