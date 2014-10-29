// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    public class HostStartTests
    {
        [Fact]
        public void Queue_IfNameIsInvalid_ThrowsDuringIndexing()
        {
            IStorageAccount account = CreateFakeStorageAccount();
            TaskCompletionSource<object> backgroundTaskSource = new TaskCompletionSource<object>();
            IServiceProvider serviceProvider = FunctionalTest.CreateServiceProviderForCallFailure(account,
                typeof(InvalidQueueNameProgram), backgroundTaskSource);

            using (JobHost host = new JobHost(serviceProvider))
            {
                // Act & Assert
                FunctionIndexingException exception = Assert.Throws<FunctionIndexingException>(() => host.Start());
                Assert.Equal("Error indexing method 'Invalid'", exception.Message);
                Exception innerException = exception.InnerException;
                Assert.IsType<ArgumentException>(innerException);
                ArgumentException argumentException = (ArgumentException)innerException;
                Assert.Equal("name", argumentException.ParamName);
                string expectedMessage = String.Format(CultureInfo.InvariantCulture,
                    "The dash (-) character may not be the first or last letter - \"-illegalname-\"{0}Parameter " +
                    "name: name", Environment.NewLine);
                Assert.Equal(expectedMessage, innerException.Message);
                Assert.Equal(TaskStatus.WaitingForActivation, backgroundTaskSource.Task.Status);
            }
        }

        private class InvalidQueueNameProgram
        {
            public static void Invalid([Queue("-IllegalName-")] CloudQueue queue)
            {
            }
        }

        private static IStorageAccount CreateFakeStorageAccount()
        {
            return new FakeStorageAccount();
        }
    }
}
