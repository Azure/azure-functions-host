// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Xunit;
using System.Reflection;
using System.Net;

namespace Microsoft.Azure.WebJobs.Logging.FunctionalTests
{
    // Helper for unit testing safe creating tables. 
    // This works through various retry and error scenarios. 
    public class SafeCreateTests
    {
        static StorageException Error(HttpStatusCode code)
        {
            return new StorageException(
                new RequestResult
                {
                    HttpStatusCode = (int)code
                },
                string.Empty,
                new Exception());
        }

        // Create Normally 
        [Fact]
        public async Task CreateNormal()
        {
            var func = MockCloudTable.GetSafeCreateMethod();

            var table = new MockCloudTable();
            table.Func = new Action[]
            {
                () => { } // Nop, success
            };

            await func(table);
        }

        // Have conflict, retry 
        [Fact]
        public async Task CreateWithConflict()
        {
            var func = MockCloudTable.GetSafeCreateMethod();
            {
                var table = new MockCloudTable();
                table.Func = new Action[]
                {
                    () => { throw Error(HttpStatusCode.Conflict); },
                    () => { },  // success
                };

                await func(table);
            }
        }

        // Fail with non 409 error from storage
        [Fact]
        public async Task FailWithStorageError()
        {
            var func = MockCloudTable.GetSafeCreateMethod();

            var exception = Error(HttpStatusCode.BadRequest);
            try
            {
                var table = new MockCloudTable();
                table.Func = new Action[]
                {
                       () => { throw exception; } // terminal abort 
                };

                await func(table);
                Assert.True(false); // should have thrown 
            }
            catch (StorageException e)
            {
                Assert.True(Object.ReferenceEquals(e, exception));
            }
        }

        // Fail with an error not from storage
        [Fact]
        public async Task FailWithNonStorageError()
        {
            var func = MockCloudTable.GetSafeCreateMethod();

            // Completely different exception  Storage error 

            var exception = new InvalidOperationException("other");
            try
            {
                var table = new MockCloudTable();
                table.Func = new Action[]
                {
                     () => { throw exception; } // terminal abort 
                };

                await func(table);

                Assert.True(false); // should have thrown 
            }
            catch (Exception e)
            {
                Assert.True(Object.ReferenceEquals(e, exception));
            }
        }


        // Fail because of too many retries. 
        [Fact]
        public async Task FailWithTimeout()
        {
            var func = MockCloudTable.GetSafeCreateMethod();
            try
            {
                var table = new MockCloudTable();
                table.Func = new Action[]
                {
                    () => { throw Error(HttpStatusCode.Conflict); },
                    () => { throw Error(HttpStatusCode.Conflict); },
                    () => { throw Error(HttpStatusCode.Conflict); },
                    () => { throw Error(HttpStatusCode.Conflict); },
                };

                await func(table);
                Assert.True(false); // should have thrown 
            }
            catch (StorageException e)
            {
                // Success
            }
        }

        // Helper for unit testing SafeCreateAsync()
        public class MockCloudTable : CloudTable
        {
            public int Counter = 0;

            public MockCloudTable() : base(new Uri("http://contoso.com"))
            {
            }

            public override Task<bool> CreateIfNotExistsAsync()
            {
                try
                {
                    this.Func[this.Counter]();
                }
                finally
                {
                    this.Counter++;
                }
                return Task.FromResult(true);
            }

            // What to do on each iteration 
            public Action[] Func;

            public static Func<CloudTable, Task> GetSafeCreateMethod()
            {
                // Pass in short timeouts 
                var method = typeof(LogFactory).Assembly.GetType("Microsoft.Azure.WebJobs.Logging.Utility").GetMethod("SafeCreateAsync", BindingFlags.Static | BindingFlags.NonPublic);
                Func<CloudTable, Task> func = (CloudTable t) => (Task)method.Invoke(null, new object[] { t, 1, 2 });
                return func;
            }
        }

    } // end SafeCreateTests
}
