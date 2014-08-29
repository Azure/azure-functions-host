// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Dashboard.Data;
using Dashboard.HostMessaging;
using Dashboard.Indexers;
using Dashboard.ViewModels;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.Azure.WebJobs.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Microsoft.Azure.WebJobs.Host.Executors;
using System.Threading;
using Dashboard.Infrastructure;

namespace Dashboard
{
    // $$$ Analysis can be expensive. Use a cache?
    public class LogAnalysis
    {
        public static void AddParameterModels(string parameterName, FunctionInstanceArgument argument, ParameterLog log,
            FunctionInstanceSnapshot snapshot, ICollection<ParamModel> parameterModels)
        {
            ParamModel model = new ParamModel
            {
                Name = parameterName,
                ArgInvokeString = argument.Value
            };

            if (log != null)
            {
                model.Status = Format(log);
            }

            if (argument.IsBlob)
            {
                model.ExtendedBlobModel = LogAnalysis.CreateExtendedBlobModel(snapshot, argument);
            }

            parameterModels.Add(model);

            // Special-case IBinder, which adds sub-parameters.
            BinderParameterLog binderLog = log as BinderParameterLog;

            if (binderLog != null)
            {
                IEnumerable<BinderParameterLogItem> items = binderLog.Items;

                if (items != null)
                {
                    int count = items.Count();
                    model.Status = String.Format(CultureInfo.CurrentCulture, "Bound {0} object{1}.", count, count != 1 ? "s" : String.Empty);

                    foreach (BinderParameterLogItem item in items)
                    {
                        if (item == null)
                        {
                            continue;
                        }

                        ParameterSnapshot itemSnapshot = HostIndexer.CreateParameterSnapshot(item.Descriptor);
                        string itemName = parameterName + ": " + itemSnapshot.AttributeText;
                        FunctionInstanceArgument itemArgument =
                            FunctionIndexer.CreateFunctionInstanceArgument(item.Value, item.Descriptor);

                        AddParameterModels(itemName, itemArgument, item.Log, snapshot, parameterModels);
                    }
                }
            }
        }

        internal static BlobBoundParamModel CreateExtendedBlobModel(FunctionInstanceSnapshot snapshot, FunctionInstanceArgument argument)
        {
            Debug.Assert(argument != null);

            if (argument.Value == null)
            {
                return null;
            }

            string[] components = argument.Value.Split(new char[] { '/' });

            if (components.Length != 2)
            {
                return null;
            }

            var blobParam = new BlobBoundParamModel();
            blobParam.IsOutput = argument.IsBlobOutput;
            blobParam.ConnectionStringKey = AmbientConnectionStringProvider.GetPrefixedConnectionStringName(argument.AccountName);

            string storageConnectionString = argument.GetStorageConnectionString();
            if (String.IsNullOrEmpty(storageConnectionString))
            {
                blobParam.IsConnectionStringMissing = true;
                return blobParam;
            }

            CloudBlockBlob blob = CloudStorageAccount.Parse(storageConnectionString)
                .CreateCloudBlobClient()
                .GetContainerReference(components[0])
                .GetBlockBlobReference(components[1]);

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
                return AspNetTaskExecutor.Execute(
                    () => BlobCausalityManager.GetWriterAsync(blob, CancellationToken.None)) ?? Guid.Empty;
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

        // Get Live information from current watcher values. 
        internal static IDictionary<string, ParameterLog> GetParameterLogs(CloudStorageAccount account, FunctionInstanceSnapshot snapshot)
        {
            if (snapshot.ParameterLogs != null)
            {
                return snapshot.ParameterLogs;
            }

            if (snapshot.ParameterLogBlob == null)
            {
                return null;
            }

            CloudBlockBlob blob = snapshot.ParameterLogBlob.GetBlockBlob(account);

            string contents;

            try
            {
                contents = blob.DownloadText();
            }
            catch (StorageException exception)
            {
                // common case, no status information written.
                if (exception.IsNotFound())
                {
                    return null;
                }
                else
                {
                    throw;
                }
            }

            try
            {
                return JsonConvert.DeserializeObject<IDictionary<string, ParameterLog>>(contents);
            }
            catch
            {
                // Not fatal. 
                // This could happen if the app wrote a corrupted log. 
                return null;
            }
        }

        private static string Format(ParameterLog log)
        {
            ReadBlobParameterLog readBlobLog = log as ReadBlobParameterLog;

            if (readBlobLog != null)
            {
                return Format(readBlobLog);
            }

            WriteBlobParameterLog writeBlobLog = log as WriteBlobParameterLog;

            if (writeBlobLog != null)
            {
                return Format(writeBlobLog);
            }

            TableParameterLog tableLog = log as TableParameterLog;

            if (tableLog != null)
            {
                return Format(tableLog);
            }

            TextParameterLog textLog = log as TextParameterLog;

            if (textLog != null)
            {
                return textLog.Value;
            }

            return null;
        }

        private static string Format(ReadBlobParameterLog log)
        {
            Debug.Assert(log != null);
            StringBuilder builder = new StringBuilder();
            long bytesRead = log.BytesRead;
            double complete = bytesRead * 100.0 / log.Length;
            builder.AppendFormat("Read {0:n0} bytes ({1:0.00}% of total). ", bytesRead, complete);
            AppendNetworkTime(builder, log.ElapsedTime);
            return builder.ToString();
        }

        internal static void AppendNetworkTime(StringBuilder builder, TimeSpan elapsed)
        {
            if (elapsed == TimeSpan.Zero)
            {
                return;
            }

            builder.Append("(about ");

            string unitName;
            int unitCount;

            if (elapsed > TimeSpan.FromMinutes(55)) // it is about an hour, right?
            {
                unitName = "hour";
                unitCount = (int)Math.Round(elapsed.TotalHours);
            }
            else if (elapsed > TimeSpan.FromSeconds(55)) // it is about a minute, right?
            {
                unitName = "minute";
                unitCount = (int)Math.Round(elapsed.TotalMinutes);
            }
            else if (elapsed > TimeSpan.FromMilliseconds(950)) // it is about a second, right?
            {
                unitName = "second";
                unitCount = (int)Math.Round(elapsed.TotalSeconds);
            }
            else
            {
                unitName = "millisecond";
                unitCount = Math.Max((int)Math.Round(elapsed.TotalMilliseconds), 1);
            }
            builder.AppendFormat(CultureInfo.CurrentCulture, "{0} {1}{2}", unitCount, unitName, unitCount > 1 ? "s" : String.Empty);
            builder.Append(" spent on I/O)");
        }

        private static string Format(WriteBlobParameterLog log)
        {
            Debug.Assert(log != null);

            if (!log.WasWritten)
            {
                return "Nothing was written.";
            }
            else
            {
                return String.Format(CultureInfo.CurrentCulture, "Wrote {0:n0} bytes.", log.BytesWritten);
            }
        }

        private static string Format(TableParameterLog log)
        {
            Debug.Assert(log != null);

            return String.Format(CultureInfo.CurrentCulture, "Wrote {0} {1}. ({2})", log.EntitiesWritten,
                log.EntitiesWritten == 1 ? "entity" : "entities", log.ElapsedWriteTime);
        }
    }
}
