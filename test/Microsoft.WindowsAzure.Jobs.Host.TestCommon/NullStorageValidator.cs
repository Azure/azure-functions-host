namespace Microsoft.WindowsAzure.Jobs.Host.TestCommon
{
    // StorageValidator that skips validation. 
    public class NullStorageValidator : IStorageValidator
    {
        public bool TryValidateConnectionString(string connectionString, out string validationErrorMessage)
        {
            validationErrorMessage = null;
            return true;
        }
    }
}