using System;
using System.Collections.Generic;
using System.Linq;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

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
        internal static ParamModel[] GetParamInfo(FunctionSnapshot function)
        {
            var parameters = function.Parameters;

            ParamModel[] ps = new ParamModel[parameters.Count];
            int index = 0;

            foreach (KeyValuePair<string, ParameterSnapshot> parameter in parameters)
            {
                ps[index] = new ParamModel
                {
                    Name = parameter.Key,
                    Description = parameter.Value.Description
                };
                index++;
            }
            return ps;
        }

        internal static void ApplyRuntimeInfo(FunctionInstanceSnapshot snapshot, ParamModel[] parameterModels)
        {
            foreach (KeyValuePair<string, FunctionInstanceArgument> pair in snapshot.Arguments)
            {
                ParamModel parameterModel = parameterModels.FirstOrDefault(p => p.Name == pair.Key);

                if (parameterModel == null)
                {
                    continue;
                }

                FunctionInstanceArgument argument = pair.Value;
                parameterModel.ArgInvokeString = argument.Value;

                // If arg is a blob, provide a blob-aware link to explore that further.
                if (argument.IsBlob)
                {
                    parameterModel.ExtendedBlobModel = CreateExtendedBlobModel(snapshot, argument);
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
                return BlobCausalityLogger.GetWriter(blob);
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

        internal static void ApplySelfWatchInfo(CloudStorageAccount account, FunctionInstanceSnapshot snapshot, ParamModel[] parameterModels)
        {
            // Get selfwatch information
            IDictionary<string, string> selfwatch = GetParameterSelfWatch(account, snapshot);

            if (selfwatch == null)
            {
                return;
            }

            foreach (KeyValuePair<string, string> pair in selfwatch)
            {
                ParamModel parameterModel = parameterModels.FirstOrDefault(p => p.Name == pair.Key);

                if (parameterModel == null)
                {
                    continue;
                }

                parameterModel.SelfWatch = pair.Value;
            }
        }

        // Get Live information from current self-watch values. 
        private static IDictionary<string, string> GetParameterSelfWatch(CloudStorageAccount account, FunctionInstanceSnapshot snapshot)
        {
            if (snapshot.ParameterLogs != null)
            {
                return ToStringDictionary(snapshot.ParameterLogs);
            }

            if (snapshot.ParameterLogBlob == null)
            {
                return null;
            }

            CloudBlockBlob blob = snapshot.ParameterLogBlob.GetBlockBlob(snapshot.StorageConnectionString);

            string contents;

            try
            {
                contents = blob.DownloadText();
            }
            catch (StorageException exception)
            {
                // common case, no selfwatch information written.
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            IDictionary<string, ParameterLog> logs;

            try
            {
                logs = JsonConvert.DeserializeObject<IDictionary<string, ParameterLog>>(contents);
            }
            catch
            {
                // Not fatal. 
                // This could happen if the app wrote a corrupted log. 
                return null;
            }

            return ToStringDictionary(logs);
        }

        private static IDictionary<string, string> ToStringDictionary(IDictionary<string, ParameterLog> parameterLogs)
        {
            Dictionary<string, string> logs = new Dictionary<string, string>();

            foreach (KeyValuePair<string, ParameterLog> status in parameterLogs)
            {
                TextParameterLog textLog = status.Value as TextParameterLog;

                if (textLog != null)
                {
                    logs.Add(status.Key, textLog.Value);
                }
            }

            return logs;
        }

        private class BlobParameterData
        {
            public bool IsInput { get; set; }

            public CloudBlockBlob Blob { get; set; }
        }
    }
}
