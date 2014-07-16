// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // Scans storage logs for blob writes
    internal class BlobLogListener
    {
        const string LogStartTime = "StartTime";
        const string LogEndTime = "EndTime";
        const string LogType = "LogType";

        private readonly CloudBlobClient _blobClient;
        private readonly HashSet<string> _scannedBlobNames = new HashSet<string>();

        // This will throw if the client credentials are not valid. 
        public BlobLogListener(CloudBlobClient blobClient)
        {
            _blobClient = blobClient;

            EnableLogging(_blobClient);
        }

        public CloudBlobClient Client
        {
            get { return _blobClient; }
        }

        // Instance method has caching on it. 
        public IEnumerable<ICloudBlob> GetRecentBlobWrites(int hoursWindow = 2)
        {
            var time = DateTime.UtcNow; // will scan back 2 hours, which is enough to deal with clock sqew
            foreach (var blob in ListRecentLogFiles(_blobClient, time, hoursWindow))
            {
                bool isAdded = _scannedBlobNames.Add(blob.Name);
                if (!isAdded)
                {
                    continue;
                }

                // Need to clear out cache. 
                if (_scannedBlobNames.Count > 100 * 1000)
                {
                    _scannedBlobNames.Clear();
                }

                foreach (var row in ParseLog(blob))
                {
                    bool isBlobWrite = IsBlobWrite(row);

                    if (isBlobWrite)
                    {
                        var path = row.ToPath();
                        if (path != null)
                        {
                            CloudBlobContainer container = _blobClient.GetContainerReference(path.ContainerName);
                            yield return container.GetBlockBlobReference(path.BlobName);
                        }
                    }
                }
            }
        }

        private static bool IsBlobWrite(LogRow row)
        {
            bool isBlobWrite =
               ((row.OperationType == OperationType.PutBlob) ||
                (row.OperationType == OperationType.CopyBlob) ||
                (row.OperationType == OperationType.CopyBlobDestination) ||
                (row.OperationType == OperationType.CopyBlobSource) ||
                (row.OperationType == OperationType.SetBlobMetadata) ||
                (row.OperationType == OperationType.SetBlobProperties));
            return isBlobWrite;
        }

        // Return a search prefix for the given start,end time. 
        //  $logs/YYYY/MM/DD/HH00
        private static string GetSearchPrefix(string service, DateTime startTime, DateTime endTime)
        {
            StringBuilder prefix = new StringBuilder("$logs/");

            prefix.AppendFormat("{0}/", service);

            // if year is same then add the year
            if (startTime.Year == endTime.Year)
            {
                prefix.AppendFormat("{0}/", startTime.Year);
            }
            else
            {
                return prefix.ToString();
            }

            // if month is same then add the month
            if (startTime.Month == endTime.Month)
            {
                prefix.AppendFormat("{0:D2}/", startTime.Month);
            }
            else
            {
                return prefix.ToString();
            }

            // if day is same then add the day
            if (startTime.Day == endTime.Day)
            {
                prefix.AppendFormat("{0:D2}/", startTime.Day);
            }
            else
            {
                return prefix.ToString();
            }

            // if hour is same then add the hour
            if (startTime.Hour == endTime.Hour)
            {
                prefix.AppendFormat("{0:D2}00", startTime.Hour);
            }

            return prefix.ToString();
        }

        // Scan this hour and last hour
        // This lets us use prefix scans. $logs/Blob/YYYY/MM/DD/HH00/nnnnnn.log
        // Logs are about 6 an hour, so we're only scanning about 12 logs total. 
        // $$$ If logs are large, we can even have a cache of "already scanned" logs that we skip. 
        public static List<ICloudBlob> ListRecentLogFiles(CloudBlobClient blobClient, DateTime startTimeForSearch, int hoursWindow = 2)
        {
            string serviceName = "blob";

            List<ICloudBlob> selectedLogs = new List<ICloudBlob>();

            var lastHour = startTimeForSearch;
            for (int i = 0; i < hoursWindow; i++)
            {
                var prefix = GetSearchPrefix(serviceName, lastHour, lastHour);
                GetLogsWithPrefix(selectedLogs, blobClient, prefix);
                lastHour = lastHour.AddHours(-1);
            }

            return selectedLogs;
        }

        // Populate the List<> with blob logs for the given prefix. 
        // http://blogs.msdn.com/b/windowsazurestorage/archive/2011/08/03/windows-azure-storage-logging-using-logs-to-track-storage-requests.aspx
        private static void GetLogsWithPrefix(List<ICloudBlob> selectedLogs, CloudBlobClient blobClient, string prefix)
        {
            // List the blobs using the prefix
            IEnumerable<IListBlobItem> blobs = blobClient.ListBlobs(prefix,
                useFlatBlobListing: true, blobListingDetails: BlobListingDetails.Metadata);


            // iterate through each blob and figure the start and end times in the metadata
            foreach (IListBlobItem item in blobs)
            {
                ICloudBlob log = item as ICloudBlob;
                if (log != null)
                {
                    // we will exclude the file if the file does not have log entries in the interested time range.
                    string logType = log.Metadata[LogType];
                    bool hasWrites = logType.Contains("write");

                    if (hasWrites)
                    {
                        selectedLogs.Add(log);
                    }
                }
            }
        }

        // Given a log file (as a blob), parse it and return a series of LogRows. 
        public static IEnumerable<LogRow> ParseLog(ICloudBlob blob)
        {
            using (TextReader tr = new StreamReader(blob.OpenRead()))
            {
                while (true)
                {
                    string line = tr.ReadLine();
                    if (line == null)
                    {
                        break;
                    }
                    yield return LogRow.Parse(line);
                }
            }
        }

        // http://blogs.msdn.com/b/windowsazurestorage/archive/2011/08/03/windows-azure-storage-logging-using-logs-to-track-storage-requests.aspx
        public static List<ICloudBlob> ListLogFiles(CloudBlobClient blobClient, string serviceName, DateTime startTimeForSearch, DateTime endTimeForSearch)
        {
            List<ICloudBlob> selectedLogs = new List<ICloudBlob>();

            // form the prefix to search. Based on the common parts in start and end time, this prefix is formed
            string prefix = GetSearchPrefix(serviceName, startTimeForSearch, endTimeForSearch);

            // List the blobs using the prefix
            IEnumerable<IListBlobItem> blobs = blobClient.ListBlobs(prefix,
                useFlatBlobListing: true, blobListingDetails: BlobListingDetails.Metadata);


            // iterate through each blob and figure the start and end times in the metadata
            foreach (IListBlobItem item in blobs)
            {
                ICloudBlob log = item as ICloudBlob;
                if (log != null)
                {
                    // we will exclude the file if the file does not have log entries in the interested time range.
                    DateTime startTime = DateTime.Parse(log.Metadata[LogStartTime], CultureInfo.InvariantCulture).ToUniversalTime();
                    DateTime endTime = DateTime.Parse(log.Metadata[LogEndTime], CultureInfo.InvariantCulture).ToUniversalTime();

                    string logType = log.Metadata[LogType];
                    bool hasWrites = logType.Contains("write");

                    if (hasWrites)
                    {
                        bool exclude = (startTime > endTimeForSearch || endTime < startTimeForSearch);
                        if (!exclude)
                        {
                            selectedLogs.Add(log);
                        }
                    }
                }
            }

            return selectedLogs;
        }

        public static void EnableLogging(CloudBlobClient blobClient)
        {
            var sp = blobClient.GetServiceProperties();

            // Merge write onto it. 
            var lp = sp.Logging;
            if (lp.LoggingOperations == LoggingOperations.None)
            {
                // First activating. Be sure to set a retention policy if there isn't one. 
                lp.RetentionDays = 7;
                lp.LoggingOperations |= LoggingOperations.Write;

                // Leave metrics untouched           

                blobClient.SetServiceProperties(sp);
            }
        }
    }
}
