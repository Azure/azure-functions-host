using System;
using System.Globalization;

namespace Microsoft.WindowsAzure.Jobs
{
    class DefaultStorageValidator : IStorageValidator
    {
        public bool RequireLogging { get; set; }

        public void Validate(string dataConnectionString, string runtimeConnectionString)
        {
            if (dataConnectionString == null)
            {
                throw new InvalidOperationException(String.Format(CultureInfo.CurrentCulture, "User account connection string is missing. This can be set via the '{0}' connection string or via the constructor.", JobHost.DataConnectionStringName));
            }
            Utility.ValidateConnectionString(dataConnectionString);
            if (runtimeConnectionString != null)
            {
                if (runtimeConnectionString != dataConnectionString)
                {
                    Utility.ValidateConnectionString(runtimeConnectionString);
                }
            }
            else
            {
                if (RequireLogging)
                {
                    var msg =
                        string.Format(
                            "Windows Azure Jobs Runtime connection string is missing. " +
                            "You can specify it by setting a connection string named '{0}' in the connectionStrings section of the .config file, " +
                            "or with an environment variable named '{0}', or by using the constructor for JobHost that accepts connection strings.",
                            JobHost.LoggingConnectionStringName);
                    throw new InvalidOperationException(msg);
                }
            }
        }
    }
}
