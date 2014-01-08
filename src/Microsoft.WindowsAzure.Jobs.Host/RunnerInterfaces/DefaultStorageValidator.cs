using System;

namespace Microsoft.WindowsAzure.Jobs
{
    class DefaultStorageValidator : IStorageValidator
    {
        public void Validate(string dataConnectionString, string runtimeConnectionString)
        {
            if (dataConnectionString == null)
            {
                throw new InvalidOperationException(string.Format("User account connection string is missing. This can be set via the '{0}' connection string or via the constructor.", JobHost.DataConnectionStringName));
            }
            Utility.ValidateConnectionString(dataConnectionString);
            if (runtimeConnectionString != null)
            {
                if (runtimeConnectionString != dataConnectionString)
                {
                    Utility.ValidateConnectionString(runtimeConnectionString);
                }
            }
        }
    }
}
