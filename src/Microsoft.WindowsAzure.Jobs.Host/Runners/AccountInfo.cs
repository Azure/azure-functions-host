namespace Microsoft.WindowsAzure.Jobs
{
    // Default class for explicitly providing account information. 
    internal class AccountInfo : IAccountInfo
    {
        // Set via properties
        public AccountInfo()
        {
        }

        // Initialize around another source
        public AccountInfo(IAccountInfo accountInfo)
        {
            this.AccountConnectionString = accountInfo.AccountConnectionString;
            this.WebDashboardUri = accountInfo.WebDashboardUri;
        }

        public string AccountConnectionString { get; set; }
        public string WebDashboardUri { get; set; }
    }
}
