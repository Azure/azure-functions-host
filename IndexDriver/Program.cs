using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using Orchestrator;
using RunnerInterfaces;

namespace IndexDriver
{
    // $$$ Use this
    public class IndexDriverInput
    {
        // Describes the cloud resource for what to be indexed.
        // This includes a blob download (or upload!) location 
        public IndexRequestPayload Request { get; set; }

        // This can be used to download assemblies locally for inspection.
        public string LocalCache { get; set; }
    }

    public class IndexResults
    {
        public IndexResults()
        {
        }

        public FunctionIndexEntity[] NewFunctions { get; set; }

        public FunctionIndexEntity[] UpdatedFunctions { get; set; }

        public FunctionIndexEntity[] DeletedFunctions { get; set; }

        public string[] Errors { get; set; }
    }

    // Do indexing in app. 
    public class Program
    {
        public static void Main(string[] args)
        {
            var client = new Utility.ProcessExecuteArgs<IndexDriverInput, IndexResults>(args);

            IndexDriverInput descr = client.Input;
            var result = MainWorker(descr);

            client.Result = result;
        }

        public static IndexResults MainWorker(IndexDriverInput input)
        {
            string urlLogger = null;
            StringWriter buffer = null;                        

            try
            {
                string localCache = input.LocalCache;
                IndexRequestPayload payload = input.Request;

                // ### This can go away now that we have structured return results
                urlLogger = payload.Writeback;
                if (urlLogger != null)
                {
                    Console.WriteLine("Logging output to: {0}", urlLogger);
                    CloudBlob blob = new CloudBlob(urlLogger);
                    blob.UploadText(string.Format("Beginning indexing {0}", payload.Blobpath));
                }
                                
                if (payload.Writeback != null)
                {
                    buffer = new StringWriter();
                    Console.SetOut(buffer);
                }
                                
                Console.WriteLine("indexing: {0}", payload.Blobpath);

                var account = Secrets.GetAccount();
                var settings = new LoggingCloudIndexerSettings
                {
                    Account = account,
                    FunctionIndexTableName = Secrets.FunctionIndexTableName
                };

                var binderLookupTable = Services.GetBinderTable();

                HashSet<string> funcsBefore = settings.GetFuncSet();

                Indexer i = new Indexer(settings);

                var path = new CloudBlobPath(payload.Blobpath);

                var cd = new CloudBlobDescriptor
                {
                    AccountConnectionString = payload.AccountConnectionString,
                    ContainerName = path.ContainerName,
                    BlobName = path.BlobName
                };

                i.IndexContainer(cd, localCache, binderLookupTable);

                // Log what changes happned (added, removed, updated)
                // Compare before and after
                HashSet<string> funcsAfter = settings.GetFuncSet();

                var funcsTouched = from func in settings._funcsTouched select func.ToString();
                PrintDifferences(funcsBefore, funcsAfter, funcsTouched);
             
                Console.WriteLine("DONE: SUCCESS");
            }
            catch (InvalidOperationException e)
            {
                // Expected user error
                Console.WriteLine("Error during indexing: {0}", e);
                Console.WriteLine("DONE: FAILED");
            }
            catch (Exception e)
            {
                // For other errors, be specific
                Console.WriteLine("Error during indexing: {0}", e);
                Console.WriteLine(e.GetType().FullName);
                Console.WriteLine(e.StackTrace);
                Console.WriteLine("DONE: FAILED");
            }

            if ((urlLogger != null) && (buffer != null))
            {
                CloudBlob blob = new CloudBlob(urlLogger);
                blob.UploadText(buffer.ToString());
            }

            return new IndexResults();
        }       

        class LoggingCloudIndexerSettings : CloudIndexerSettings
        {
            public List<FunctionIndexEntity> _funcsTouched  = new List<FunctionIndexEntity>();

            public List<Type> BinderTypes = new List<Type>();

            public override void Add(FunctionIndexEntity func)
            {
                _funcsTouched.Add(func);
                base.Add(func);
            }

            public HashSet<string> GetFuncSet()
            {
                HashSet<string> funcsAfter = new HashSet<string>();
                funcsAfter.UnionWith(from func in this.ReadFunctionTable() select func.ToString());
                return funcsAfter;
            }          
        }

        static void PrintDifferences(IEnumerable<string> funcsBefore, IEnumerable<string> funcsAfter, IEnumerable<string> funcsTouched)
        {
            // Removed. In before, not in after
            var setRemoved = new HashSet<string>(funcsBefore);
            setRemoved.ExceptWith(funcsAfter);

            if (setRemoved.Count > 0)
            {
                Console.WriteLine("Functions removed:");
                foreach (var func in setRemoved)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }

            // Added
            var setAdded = new HashSet<string>(funcsAfter);
            setAdded.ExceptWith(funcsBefore);

            if (setAdded.Count > 0)
            {
                Console.WriteLine("Functions added:");
                foreach (var func in setAdded)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }

            // Touched (not including added)
            HashSet<string> setTouched = new HashSet<string>(funcsTouched);
            setTouched.ExceptWith(setAdded);
            if (setTouched.Count > 0)
            {
                Console.WriteLine("Functions updated:");
                foreach (var func in setTouched)
                {
                    Console.WriteLine("  {0}", func);
                }
                Console.WriteLine();
            }
        }
    }
}
