namespace Microsoft.Azure.Jobs
{
    // Provide underlying access to account information. 
    internal interface IAccountInfo
    {
        // azure storage Account for the storage items the service uses to operate. 
        // This is a secret.
        string AccountConnectionString { get; }
    }
}
