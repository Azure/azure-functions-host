namespace Microsoft.Azure.Jobs
{
    internal interface IStorageValidator
    {
        bool TryValidateConnectionString(string connectionString, out string validationErrorMessage);
    }
}
