namespace Microsoft.Azure.Jobs
{
    /// <summary>
    /// Store all sensitive information in one spot.  
    /// </summary>
    internal class Credentials
    {
        /// <summary>
        /// The azure storage account connection string that blob and queue triggers bind against. 
        /// </summary>
        public string AccountConnectionString { get; set; }

        public string ServiceBusConnectionString { get; set; }
    }
}
