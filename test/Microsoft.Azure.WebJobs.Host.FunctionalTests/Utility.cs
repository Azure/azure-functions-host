// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.TestCommon;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    static class Utility
    {
        // Helper for quickly testing indexing errors 

        public static void AssertIndexingError<TProgram>(string methodName, string expectedErrorMessage)
        {
            // Need to pass an account to get passed initial validation checks. 
            IStorageAccount account = new FakeStorageAccount();
            var host = TestHelpers.NewJobHost<TProgram>(account);

            try
            {
                // Indexing is lazy, so must actually try a call first. 
                host.Call(methodName);
            }
            catch (FunctionIndexingException e)
            {
                string functionName = typeof(TProgram).Name + "." + methodName;
                Assert.Equal("Error indexing method '" + functionName + "'", e.Message);
                Assert.Equal(expectedErrorMessage, e.InnerException.Message);
                return;
            }
            Assert.True(false, "Invoker should have failed");
        }
    }
}
