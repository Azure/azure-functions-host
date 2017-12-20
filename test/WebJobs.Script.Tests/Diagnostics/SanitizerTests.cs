using Microsoft.Azure.WebJobs.Logging;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Diagnostics
{
    public class SanitizerTests
    {
        private const string AssemblyLoadErrorWithAllowedToken = "System.AggregateException: aggregate error --->System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.Azure.WebJobs.Host, Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35' or one of its dependencies.The system cannot find the file specified.at System.Reflection.RuntimeAssembly._nLoad(AssemblyName fileName, String codeBase, Evidence assemblySecurity, RuntimeAssembly locationHint, StackCrawlMark & stackMark, IntPtr pPrivHostBinder, Boolean throwOnFileNotFound, Boolean forIntrospection, Boolean suppressSecurityChecks)";
        private const string AssemblyLoadError = "System.AggregateException: aggregate error --->System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.Azure.WebJobs.Host, Version=2.0.0.0, Culture=neutral, Token=31bf3856ad364e35' or one of its dependencies.The system cannot find the file specified.at System.Reflection.RuntimeAssembly._nLoad(AssemblyName fileName, String codeBase, Evidence assemblySecurity, RuntimeAssembly locationHint, StackCrawlMark & stackMark, IntPtr pPrivHostBinder, Boolean throwOnFileNotFound, Boolean forIntrospection, Boolean suppressSecurityChecks)";
        private const string AssemblyLoadErrorSanitized = "System.AggregateException: aggregate error --->System.IO.FileNotFoundException: Could not load file or assembly 'Microsoft.Azure.WebJobs.Host, Version=2.0.0.0, Culture=neutral, [Hidden Credential]' or one of its dependencies.The system cannot find the file specified.at System.Reflection.RuntimeAssembly._nLoad(AssemblyName fileName, String codeBase, Evidence assemblySecurity, RuntimeAssembly locationHint, StackCrawlMark & stackMark, IntPtr pPrivHostBinder, Boolean throwOnFileNotFound, Boolean forIntrospection, Boolean suppressSecurityChecks)";
        private const string StringWithConnectionString = "{ \"AzureWebJobsStorage\": \"DefaultEndpointsProtocol=https;AccountName=testAccount1;AccountKey=mykey1;EndpointSuffix=core.windows.net\", \"AnotherKey\": \"AnotherValue\" }";
        private const string SanitizedConnectionString = "{ \"AzureWebJobsStorage\": \"[Hidden Credential]\", \"AnotherKey\": \"AnotherValue\" }";
        private const string ExceptionWithSecret = "Invalid string: \"DefaultEndpointsProtocol=https;AccountName=testaccount;AccountKey=testkey;BlobEndpoint=https://testaccount.blob.core.windows.net/;QueueEndpoint=https://testaccount.queue.core.windows.net/;TableEndpoint=https://testaccount.table.core.windows.net/;FileEndpoint=https://testaccount.file.core.windows.net/;\"";
        private const string SanitizedException = "Invalid string: \"[Hidden Credential]\"";

        [Theory]
        [InlineData(AssemblyLoadErrorWithAllowedToken, AssemblyLoadErrorWithAllowedToken)]
        [InlineData(AssemblyLoadError, AssemblyLoadErrorSanitized)]
        [InlineData(StringWithConnectionString, SanitizedConnectionString)]
        [InlineData(ExceptionWithSecret, SanitizedException)]
        public void SanitizedStringsTest(string inputString, string expectedSanitizedString)
        {
            string sanitizedString = Sanitizer.Sanitize(inputString);

            Assert.Equal(expectedSanitizedString, sanitizedString);
        }
    }
}
