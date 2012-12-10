using System;
using System.Collections.Generic;
using System.IO;
using DaasEndpoints;
using Executor;
using Orchestrator;
using RunnerHost;
using RunnerInterfaces;

namespace WebFrontEnd.Controllers
{
    public class ParamModel
    {
        // Static info
        public string Name { get; set; }
        public string Description { get; set; }

        // human-readably string version of runtime information.
        // Links provide optional runtime information for further linking to explore arg.
        public string ArgInvokeString { get; set; }
        public CloudBlobDescriptor ArgBlobLink { get; set; }
                
        // Runtime info. This can be structured to provide rich hyperlinks.
        public string SelfWatch { get; set; }
    }

    // $$$ Analysis can be expensive. Use a cache?
    public class LogAnalysis
    {
        private static Services GetServices()
        {
            AzureRoleAccountInfo accountInfo = new AzureRoleAccountInfo();
            return new Services(accountInfo);
        }

        // Gets static information
        public static ParamModel[] GetParamInfo(FunctionIndexEntity func)
        {
            var flows = func.Flow.Bindings;

            int len = flows.Length;
            ParamModel[] ps = new ParamModel[len];

            for (int i = 0; i < len; i++)
            {
                var flow = flows[i];
                string msg = flow.Description;            

                ps[i] = new ParamModel
                {
                    Name = flow.Name,
                    Description = msg,
                };
            }
            return ps;
        }

        public static void ApplyRuntimeInfo(ParameterRuntimeBinding[] args, ParamModel[] ps)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                string s = arg.ConvertToInvokeString();
                if (s == null)
                {
                    s = "???";
                    // This should be rare. Maybe a case that's not implemented. Or a corrupt arg in the queue. 
                    // ###
                }
                else
                {
                    // If arg is a blob, provide a blob-aware link to explore that further.
                    var blobArg = arg as BlobParameterRuntimeBinding;
                    if (blobArg != null)
                    {
                        ps[i].ArgBlobLink = blobArg.Blob;
                    }
                }
                ps[i].ArgInvokeString = s;                
            }
        }

        public static void ApplySelfWatchInfo(FunctionInvokeRequest instance, ParamModel[] ps)
        {
            // Get selfwatch information
            string[] selfwatch = GetParameterSelfWatch(instance);

            if (selfwatch == null)
            {
                return;
            }

            for (int i = 0; i < selfwatch.Length; i++)
            {
                ps[i].SelfWatch = selfwatch[i] ?? string.Empty;
            }
        }

        // Get Live information from current self-watch values. 
        public static string[] GetParameterSelfWatch(FunctionInvokeRequest instance)
        {
            if (instance.ParameterLogBlob == null)
            {
                return null;
            }

            try
            {

                var blob = instance.ParameterLogBlob.GetBlob();

                if (!Utility.DoesBlobExist(blob))
                {
                    return null; // common case, no selfwatch information written.
                }

                var content = blob.DownloadText();
                TextReader tr = new StringReader(content);

                List<string> list = new List<string>();
                while (true)
                {
                    var line = tr.ReadLine();
                    if (line == null)
                    {
                        if (list.Count != instance.Args.Length)
                        {
                            // Corrupted selfwatch information. 
                            // Return an error message so that we know something went wrong. 
                            var x = new string[instance.Args.Length];
                            for (int i = 0; i < instance.Args.Length; i++)
                            {
                                x[i] = "???";
                            }
                            return x;
                        }

                        return list.ToArray();
                    }
                    list.Add(line);
                }
            }
            catch
            {
                // Not fatal. 
                // This could happen if the app wrote a corrupted log. 
                return null; 
            }
        }


        // Mine all the logs to determine blob readers and writers
        public static LogBlobModel Compute(CloudBlobDescriptor blobPathAndAccount, IEnumerable<ExecutionInstanceLogEntity> logs)
        {
            string blobPath = blobPathAndAccount.GetId();

            List<ExecutionInstanceLogEntity> reader = new List<ExecutionInstanceLogEntity>();
            List<ExecutionInstanceLogEntity> writer = new List<ExecutionInstanceLogEntity>();

            foreach (var log in logs)
            {
                FunctionInvokeRequest instance = log.FunctionInstance;
                ParameterRuntimeBinding[] args = instance.Args;
                var descriptor = GetServices().Lookup(instance.Location);
                var flows = descriptor.Flow.Bindings;

                for (int i = 0; i < args.Length; i++)
                {
                    var blobBinding = flows[i] as BlobParameterStaticBinding;
                    if (blobBinding == null)
                    {
                        continue;
                    }

                    ParameterRuntimeBinding arg = args[i];
                    var blobArg = arg as BlobParameterRuntimeBinding;
                    if (blobArg == null)
                    {
                        continue;
                    }

                    string path = blobArg.Blob.GetId();
                    if (string.Compare(path, blobPath, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        if (blobBinding.IsInput)
                        {
                            reader.Add(log);
                        }
                        else
                        {
                            writer.Add(log);
                        }
                    }                    
                }
            }

            return new LogBlobModel
            {
                BlobPath = blobPath,
                Readers = reader.ToArray(),
                Writer = writer.ToArray()
            };
        }
    }

}