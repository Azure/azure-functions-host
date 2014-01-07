using System;

namespace Microsoft.WindowsAzure.Jobs
{
    class DefaultStorageValidator : IStorageValidator
    {
        public void Validate(string userAccountConnectionString, string loggingAccountConnectionString)
        {
            if (userAccountConnectionString == null)
            {
                throw new InvalidOperationException("User account connection string is missing. This can be set via the 'SimpleBatchUserACS' appsetting or via the constructor.");
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