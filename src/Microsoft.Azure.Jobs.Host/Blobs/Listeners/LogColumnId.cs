namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // Format for 1.0 logs:
    // <version-number>;<request-start-time>;<operation-type>;<request-status>;<http-status-code>;<end-to-end-latency-in-ms>;<server-latency-in-ms>;<authentication-type>;<requester-account-name>;<owner-account-name>;<service-type>;<request-url>;<requested-object-key>;<request-id-header>;<operation-count>;<requester-ip-address>;<request-version-header>;<request-header-size>;<request-packet-size>;<response-header-size>;<response-packet-size>;<request-content-length>;<request-md5>;<server-md5>;<etag-identifier>;<last-modified-time>;<conditions-used>;<user-agent-header>;<referrer-header>;<client-request-id> 
    // Schema defined at: http://msdn.microsoft.com/en-us/library/windowsazure/hh343259.aspx
    internal enum LogColumnId
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
        RequestedObjectKey = 12, // This is the BlobPath, specifies the blob name! eg, /Account/Container/Blob
        RequestIdHeader = 13, // a GUID
        OperationCount = 14,

        // Rest of the fields:
        // ;<requester-ip-address>;<request-version-header>;<request-header-size>;<request-packet-size>;<response-header-size>;<response-packet-size>;<request-content-length>;<request-md5>;<server-md5>;<etag-identifier>;<last-modified-time>;<conditions-used>;<user-agent-header>;<referrer-header>;<client-request-id> 
    }
}
