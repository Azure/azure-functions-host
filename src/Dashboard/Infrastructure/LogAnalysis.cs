using System;
using System.Collections.Generic;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Microsoft.Azure.Jobs;
using Microsoft.Azure.Jobs.Protocols;
using Microsoft.Azure.Jobs.Storage;
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
        internal static BlobBoundParamModel CreateExtendedBlobModel(FunctionInstanceSnapshot snapshot, FunctionInstanceArgument argument)
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
            blobParam.IsOutput = argument.IsBlobOutput;

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
                return BlobCausalityLogger.GetWriter(blob) ?? Guid.Empty;
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

        // Get Live information from current self-watch values. 
        internal static IDictionary<string, string> GetParameterSelfWatch(FunctionInstanceSnapshot snapshot)
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
    }
}
