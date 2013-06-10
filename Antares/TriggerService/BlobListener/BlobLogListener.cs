using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using System.Linq;
using System.Threading;
using System.Text;
using Microsoft.WindowsAzure.StorageClient.Protocol;
using System.IO;
using TriggerService.Internal;

namespace TriggerService
{
    // Format for 1.0 logs:
    // <version-number>;<request-start-time>;<operation-type>;<request-status>;<http-status-code>;<end-to-end-latency-in-ms>;<server-latency-in-ms>;<authentication-type>;<requester-account-name>;<owner-account-name>;<service-type>;<request-url>;<requested-object-key>;<request-id-header>;<operation-count>;<requester-ip-address>;<request-version-header>;<request-header-size>;<request-packet-size>;<response-header-size>;<response-packet-size>;<request-content-length>;<request-md5>;<server-md5>;<etag-identifier>;<last-modified-time>;<conditions-used>;<user-agent-header>;<referrer-header>;<client-request-id> 
    // Schema defined at: http://msdn.microsoft.com/en-us/library/windowsazure/hh343259.aspx
    enum LogColumnId
    {
        VersionNumber = 0,
        RequestStartTime = 1, // DateTime
        OperationType = 2, // See list at http://msdn.microsoft.com/en-us/library/windowsazure/hh343260.aspx 
        RequestStatus = 3,
        HttpStatusCode = 4,
        EndToEndLatencyInMs = 5,
        ServerLatencyInMs = 6,
        AuthenticationType = 7, // Authenticated
        RequesterAccountName = 8,
        OwnerAccountName = 9,
        ServiceType = 10, // matches ServiceType
        RequestUrl = 11,
        RequestedObjectKey = 12, // This is the CloudBlobPath, specifies the blob name! eg, /Account/Container/Blob
        RequestIdHeader = 13, // a GUID
        OperationCount = 14,

        // Rest of the fields:
        // ;<requester-ip-address>;<request-version-header>;<request-header-size>;<request-packet-size>;<response-header-size>;<response-packet-size>;<request-content-length>;<request-md5>;<server-md5>;<etag-identifier>;<last-modified-time>;<conditions-used>;<user-agent-header>;<referrer-header>;<client-request-id> 
    }


    // See list at http://msdn.microsoft.com/en-us/library/windowsazure/hh343260.aspx
    public enum OperationType
    {
        AcquireLease,
        BreakLease,
        ClearPage,
        CopyBlob,
        CopyBlobSource,
        CopyBlobDestination,
        CreateContainer,
        DeleteBlob,
        DeleteContainer,
        GetBlob,
        GetBlobMetadata,
        GetBlobProperties,
        GetBlockList,
        GetContainerACL,
        GetContainerMetadata,
        GetContainerProperties,
        GetLeaseInfo,
        GetPageRegions,
        LeaseBlob,
        ListBlobs,
        ListContainers,
        PutBlob,
        PutBlockList,
        PutBlock,
        PutPage,
        ReleaseLease,
        RenewLease,
        SetBlobMetadata,
        SetBlobProperties,
        SetContainerACL,
        SetContainerMetadata,
        SnapshotBlob,
        SetBlobServiceProperties,
        GetBlobServiceProperties,
    }

    // Describes an entry in the storage log
    public class LogRow
    {
        public static LogRow Parse(string value)
        {
            string[] parts = value.Split(';');

            var x = new LogRow();
            x.RequestStartTime = DateTime.Parse(parts[(int)LogColumnId.RequestStartTime]);

            ServiceType serviceType;
            Enum.TryParse<ServiceType>(parts[(int)LogColumnId.ServiceType], out serviceType);
            x.ServiceType = serviceType;

            OperationType operationType;
            Enum.TryParse<OperationType>(parts[(int)LogColumnId.OperationType], out operationType);
            x.OperationType = operationType;

            x.RequestedObjectKey = parts[(int)LogColumnId.RequestedObjectKey];

            return x;
        }

        public DateTime RequestStartTime { get; set; }
        public OperationType OperationType { get; set; }
        public ServiceType ServiceType { get; set; }
        public string RequestedObjectKey { get; set; }


        // Null if not a blob. 
        public CloudBlobPath ToPath()
        {
            if (ServiceType != TriggerService.ServiceType.Blob)
            {
                return null;
            }

            // key is "/account/container/blob"
            // - it's enclosed in quotes 
            // - first token is the account name
            string key = this.RequestedObjectKey;

            int x = key.IndexOf('/', 2); // skip past opening quote (+1) and opening / (+1)
            if (x > 0)
            {
                int start = x + 1;
                string path = key.Substring(start, key.Length - start - 1); // -1 for closing quote
                return new CloudBlobPath(path);
            }
            return null;
        }
    }


    public enum ServiceType
    {
        Blob,
        Table,
        Queue
    }

    // Scans storage logs for blob writes
    public class BlobLogListener
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
        public IEnumerable<CloudBlobPath> GetRecentBlobWrites(int hoursWindow = 2)
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
                            yield return path;
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
        
        public static IEnumerable<CloudBlobPath> GetRecentBlobWrites(CloudBlobClient blobClient, int hoursWindow = 2)
        {
            var time = DateTime.UtcNow; // will scan back 2 hours, which is enough to deal with clock sqew
            foreach (var blob in ListRecentLogFiles(blobClient, time, hoursWindow))
            {
                foreach (var row in ParseLog(blob))
                {
                    bool isBlobWrite = IsBlobWrite(row);

                    if (isBlobWrite)
                    {
                        var path = row.ToPath();
                        if (path != null)
                        {
                            yield return path;
                        }
                    }
                }
            }
        }

        // Return a search prefix for the given start,end time. 
        //  $logs/YYYY/MM/DD/HH00
        static string GetSearchPrefix(string service, DateTime startTime, DateTime endTime)
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
        public static List<CloudBlob> ListRecentLogFiles(CloudBlobClient blobClient, DateTime startTimeForSearch, int hoursWindow = 2)
        {
            string serviceName = "blob";

            List<CloudBlob> selectedLogs = new List<CloudBlob>();

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
        private static void GetLogsWithPrefix(List<CloudBlob> selectedLogs, CloudBlobClient blobClient, string prefix)
        {
            // List the blobs using the prefix
            IEnumerable<IListBlobItem> blobs = blobClient.ListBlobsWithPrefix(
                prefix,
                new BlobRequestOptions()
                {
                    UseFlatBlobListing = true,
                    BlobListingDetails = BlobListingDetails.Metadata
                });


            // iterate through each blob and figure the start and end times in the metadata
            foreach (IListBlobItem item in blobs)
            {
                CloudBlob log = item as CloudBlob;
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
        public static IEnumerable<LogRow> ParseLog(CloudBlob blob)
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
        public static List<CloudBlob> ListLogFiles(CloudBlobClient blobClient, string serviceName, DateTime startTimeForSearch, DateTime endTimeForSearch)
        {
            List<CloudBlob> selectedLogs = new List<CloudBlob>();

            // form the prefix to search. Based on the common parts in start and end time, this prefix is formed
            string prefix = GetSearchPrefix(serviceName, startTimeForSearch, endTimeForSearch);

            // List the blobs using the prefix
            IEnumerable<IListBlobItem> blobs = blobClient.ListBlobsWithPrefix(
                prefix,
                new BlobRequestOptions()
                {
                    UseFlatBlobListing = true,
                    BlobListingDetails = BlobListingDetails.Metadata
                });


            // iterate through each blob and figure the start and end times in the metadata
            foreach (IListBlobItem item in blobs)
            {
                CloudBlob log = item as CloudBlob;
                if (log != null)
                {
                    // we will exclude the file if the file does not have log entries in the interested time range.
                    DateTime startTime = DateTime.Parse(log.Metadata[LogStartTime]).ToUniversalTime();
                    DateTime endTime = DateTime.Parse(log.Metadata[LogEndTime]).ToUniversalTime();

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
            }
            lp.LoggingOperations |= LoggingOperations.Write;

            // Leave metrics untouched           
            
            blobClient.SetServiceProperties(sp);
        }
    } // end class
}