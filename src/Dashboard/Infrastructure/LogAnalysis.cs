using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dashboard.Data;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard
{
    public class ParamModel
    {
        // Static info
        public string Name { get; set; }
        public string Description { get; set; }

        // human-readably string version of runtime information.
        // Links provide optional runtime information for further linking to explore arg.
        public string ArgInvokeString { get; set; }

        public BlobBoundParamModel ExtendedBlobModel { get; set; }

        // Runtime info. This can be structured to provide rich hyperlinks.
        public string SelfWatch { get; set; }
    }

    public class BlobBoundParamModel
    {
        public bool IsBlobOwnedByCurrentFunctionInstance { get; set; }
        public bool IsBlobMissing { get; set; }
        public Guid OwnerId { get; set; }
        public bool IsOutput { get; set; }
    }

    // $$$ Analysis can be expensive. Use a cache?
    public class LogAnalysis
    {
        // Gets static information
        internal static ParamModel[] GetParamInfo(FunctionDefinition func)
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

        internal static void ApplyRuntimeInfo(FunctionInstanceSnapshot snapshot, ParamModel[] ps)
        {
            for (int i = 0; i < snapshot.Arguments.Count; i++)
            {
                FunctionInstanceArgument argument = snapshot.Arguments.Values.ElementAt(i);
                ps[i].ArgInvokeString = argument.Value;

                // If arg is a blob, provide a blob-aware link to explore that further.
                if (argument.IsBlob)
                {
                    ps[i].ExtendedBlobModel = CreateExtendedBlobModel(snapshot, argument);
                }
            }
        }

        private static BlobBoundParamModel CreateExtendedBlobModel(FunctionInstanceSnapshot snapshot, FunctionInstanceArgument argument)
        {
            string[] components = argument.Value.Split(new char[] { '/' });

            if (components.Length != 2)
            {
                return null;
            }

            CloudBlockBlob blob = CloudStorageAccount.Parse(
                snapshot.StorageConnectionString).CreateCloudBlobClient().GetContainerReference(
                components[0]).GetBlockBlobReference(components[1]);

            var blobParam = new BlobBoundParamModel();

            Guid? blobWriter = GetBlobWriter(blob);

            if (!blobWriter.HasValue)
            {
                blobParam.IsBlobMissing = true;
            }
            else
            {
                blobParam.OwnerId = blobWriter.Value;
                if (blobWriter.Value == snapshot.Id)
                {
                    blobParam.IsBlobOwnedByCurrentFunctionInstance = true;
                }
            }
            return blobParam;
        }

        /// <summary>
        /// Get the id of a function invocation that wrote a given blob.
        /// </summary>
        /// <returns>The function invocation's id, or Guid.Empty if no owner is specified, or null if the blob is missing.</returns>
        private static Guid? GetBlobWriter(ICloudBlob blob)
        {
            if (blob == null)
            {
                return null;
            }

            try
            {
                return new BlobCausalityLogger().GetWriter(blob);
            }
            catch (StorageException e)
            {
                if (e.RequestInformation.HttpStatusCode == 404 || e.RequestInformation.HttpStatusCode == 400)
                {
                    // NoBlob
                    return null;
                }
                else
                {
                    throw;
                }
            }
        }

        internal static void ApplySelfWatchInfo(CloudStorageAccount account, FunctionInstanceSnapshot snapshot, ParamModel[] ps)
        {
            // Get selfwatch information
            string[] selfwatch = GetParameterSelfWatch(account, snapshot);

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
        internal static string[] GetParameterSelfWatch(CloudStorageAccount account, FunctionInstanceSnapshot snapshot)
        {
            if (snapshot.ParameterLogBlobUrl == null)
            {
                return null;
            }

            try
            {
                CloudBlockBlob blob = new CloudBlockBlob(new Uri(snapshot.ParameterLogBlobUrl), account.Credentials);

                if (!BlobClient.DoesBlobExist(blob))
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
                        if (list.Count != snapshot.Arguments.Count)
                        {
                            // Corrupted selfwatch information. 
                            // Return an error message so that we know something went wrong. 
                            var x = new string[snapshot.Arguments.Count];
                            for (int i = 0; i < snapshot.Arguments.Count; i++)
                            {
                                x[i] = "???";
                            }
                            return x;
                        }

                        return list.ToArray();
                    }

                    line = SelfWatch.DecodeSelfWatchStatus(line);

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

        private class BlobParameterData
        {
            public bool IsInput { get; set; }

            public CloudBlockBlob Blob { get; set; }
        }
    }
}
