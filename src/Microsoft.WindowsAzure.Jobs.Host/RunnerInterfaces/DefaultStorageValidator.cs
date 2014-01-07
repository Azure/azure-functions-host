using System;

namespace Microsoft.WindowsAzure.Jobs
{
    class DefaultStorageValidator : IStorageValidator
    {
        public void Validate(string userAccountConnectionString, string loggingAccountConnectionString)
        {
            if (userAccountConnectionString == null)
            {
                throw new InvalidOperationException(string.Format("User account connection string is missing. This can be set via the '{0}' connection string or via the constructor.", JobHost.DataConnectionStringName));
            }
            Utility.ValidateConnectionString(userAccountConnectionString);
            if (loggingAccountConnectionString != null)
            {
                if (loggingAccountConnectionString != userAccountConnectionString)
                {
                    Utility.ValidateConnectionString(loggingAccountConnectionString);
                }
            }
        }
    }
}
