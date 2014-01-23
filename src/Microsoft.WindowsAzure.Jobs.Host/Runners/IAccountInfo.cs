namespace Microsoft.WindowsAzure.Jobs
{
    // Provide underlying access to account information. 
    internal interface IAccountInfo
    {
        // azure storage Account for the storage items the service uses to operate. 
        // This is a secret.
        string AccountConnectionString { get; }

        // URL prefix, can be used as API for doing stuff like queing calls via ICall.
        // This may be a WebRole running in the same Azure service instance. 
        // This can be public.
        string WebDashboardUri { get; }
    }
}
