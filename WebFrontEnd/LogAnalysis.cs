using System;
using System.Collections.Generic;
using System.IO;
using DaasEndpoints;
using Executor;
using Orchestrator;
using RunnerHost;
using RunnerInterfaces;
using WebFrontEnd.Controllers;

namespace WebFrontEnd
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
        public static ParamModel[] GetParamInfo(FunctionDefinition func)
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
                string s = "???";

                try
                {
                    s = arg.ConvertToInvokeString();
                    
                    // If arg is a blob, provide a blob-aware link to explore that further.
                    var blobArg = arg as BlobParameterRuntimeBinding;
                    if (blobArg != null)
                    {
                        ps[i].ArgBlobLink = blobArg.Blob;
                    }                    
                }
                catch (NotImplementedException)
                {
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

                    line = line.Replace("; ", "\r\n");

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
                var descriptor = GetServices().GetFunctionTable().Lookup(instance.Location);
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

        // Return a function log that includes causality information. 
        public IEnumerable<ChargebackRow> GetChargebackLog(int recentCount, string storageName, IFunctionInstanceQuery logger)
        {
            Walker w = new Walker(logger);

            var query = new FunctionInstanceQueryFilter
            {
                Succeeded = true,
                AccountName = storageName
            };
            IEnumerable<ExecutionInstanceLogEntity> logs = logger.GetRecent(recentCount, query);


            List<ChargebackRow> rows = new List<ChargebackRow>();
                
            // First loop through and get row-independent stats.
            foreach (var log in logs)
            {
                ChargebackRow row = new ChargebackRow();
                rows.Add(row);
                                
                var instance = log.FunctionInstance;
                row.Name = instance.Location.GetShortName();
                row.Id = instance.Id;
                row.ParentId = instance.TriggerReason.ParentGuid;

                w.SetParent(row.Id, row.ParentId);
                
                if (instance.Args.Length > 0)
                {
                    row.FirstParam = instance.Args[0].ToString();
                }

                row.Duration = log.GetDuration().Value;
            }
            

            // Now go back and fill in GroupId
            // This is more efficient in a separate pass because the first pass pre-populated via SetParent()
            foreach (var row in rows)
            {
                Guid g = w.LookupGroupId(row.Id);
                row.GroupId = g;
            }

            return rows;
        }

        // Find GroupIds (the top-most ancestor). Has local caches to minimize # of network fetches. 
        public class Walker
        {
            private readonly Dictionary<Guid, Guid> _parentMap = new Dictionary<Guid, Guid>();
            private readonly Dictionary<Guid, Guid> _groupMap = new Dictionary<Guid, Guid>();
            private readonly IFunctionInstanceLookup _lookup;

            public Walker(IFunctionInstanceLookup lookup)
            {
                if (lookup == null)
                {
                    throw new ArgumentNullException("lookup");
                }
                _lookup = lookup;
            }

            public Guid LookupGroupId(Guid id)
            {
                Guid g;
                if (_groupMap.TryGetValue(id, out g))
                {
                    return g;
                }

                Guid parent = LookupParent(id);
                if (parent == Guid.Empty)
                {
                    g = id;
                }
                else
                {
                    g = LookupGroupId(parent);
                }
                _groupMap[id] = g;

                return g;
            }

            public Guid LookupParent(Guid child)
            {
                Guid g;
                if (_parentMap.TryGetValue(child, out g))
                {
                    return g;
                }

                var parentFunc = _lookup.Lookup(child);
                if (parentFunc == null)
                {
                    // Unknown parent
                    return Guid.Empty;
                }
                g = parentFunc.FunctionInstance.TriggerReason.ParentGuid;
                _parentMap[child] = g;
                return g;
            }

            public void SetParent(Guid child, Guid parent)
            {
                _parentMap[child] = parent;
            }
        }
    }

    // Row for a function instance. 
    public class ChargebackRow
    {
        // Function's name. This may be a shortened form, so don't parse it. 
        public string Name { get; set; }

        // Function instance Id. The definitive instance record. 
        public Guid Id { get; set; } 

        // Parent for this instance. Guid.Empty if no parent. 
        public Guid ParentId { get; set; }

        // top-most ancestor for the instance Id. 
        public Guid GroupId { get; set; } 

        // String representation of the first parameter. 
        // This can be used to infer ownership. 
        public string FirstParam { get; set; }

        // How long this function ran.
        public TimeSpan Duration { get; set; }

    }

}