// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Timers;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.Azure.WebJobs.Host.TestCommon
{
    public static class TestHelpers
    {
        public static async Task Await(Func<bool> condition, int timeout = 60 * 1000, int pollingInterval = 2 * 1000)
        {
            DateTime start = DateTime.Now;
            while (!condition())
            {
                await Task.Delay(pollingInterval);

                if ((DateTime.Now - start).TotalMilliseconds > timeout)
                {
                    throw new ApplicationException("Condition not reached within timeout.");
                }
            }
        }

        public static void WaitOne(WaitHandle handle, int timeout = 60 * 1000)
        {
            bool ok = handle.WaitOne(timeout);
            if (!ok)
            {
                // timeout. Event not signaled in time. 
                throw new ApplicationException("Condition not reached within timeout.");
            }         
        }

        public static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }

        // Test that we get an indexing error (FunctionIndexingException)  
        // functionName - the function name that has the indexing error. 
        // expectedErrorMessage - inner exception's message with details.
        // Invoking func() should cause an indexing error. 
        public static void AssertIndexingError(Action func, string functionName, string expectedErrorMessage)
        {
            try
            {
                func(); // expected indexing error
            }
            catch (FunctionIndexingException e)
            {
                Assert.Equal("Error indexing method '" + functionName + "'", e.Message);
                Assert.Equal(expectedErrorMessage, e.InnerException.Message);
                return;
            }
            Assert.True(false, "Invoker should have failed");
        }

        // Helper to quickly create a new JobHost object from a set of services. 
        // Default is pure-in-memory, good for unit testing. 
        public static TestJobHost<TProgram> NewJobHost<TProgram>(
             params object[] services
             )
        {
            var config = NewConfig(typeof(TProgram), services);
            var host = new TestJobHost<TProgram>(config);
            return host;
        }

        public static JobHostConfiguration NewConfig<TProgram>(          
          params object[] services
          )
        {
            return NewConfig(typeof(TProgram), services);
        }

        // Helper to create a JobHostConfiguraiton from a set of services. 
        // Default config, pure-in-memory
        // Default is pure-in-memory, good for unit testing. 
        public static JobHostConfiguration NewConfig(
            Type functions,
            params object[] services
            )
        {
            var config = NewConfig(services);
            config.AddServices(new FakeTypeLocator(functions));
            return config;
        }

        public static JobHostConfiguration NewConfig(
            params object[] services
        )
        {
            JobHostConfiguration config = new JobHostConfiguration()
            {
                // Pure in-memory, no storage. 
                HostId = Guid.NewGuid().ToString("n"),
                DashboardConnectionString = null,
                StorageConnectionString = null
            };
            config.AddServices(services);
            return config;
        }

        public static void AddServices(this JobHostConfiguration config, params object[] services)
        {
            // Set extensionRegistry first since other services may depend on it. 
            foreach (var obj in services)
            {
                IExtensionRegistry extensionRegistry = obj as IExtensionRegistry;
                if (extensionRegistry != null)
                {
                    config.AddService<IExtensionRegistry>(extensionRegistry);
                    break;
                }
            }

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();

            var types = new Type[] {
                typeof(IAsyncCollector<FunctionInstanceLogEntry>),
                typeof(IHostInstanceLoggerProvider),
                typeof(IFunctionInstanceLoggerProvider),
                typeof(IFunctionOutputLoggerProvider),
                typeof(IConsoleProvider),
                typeof(ITypeLocator),
                typeof(IWebJobsExceptionHandler),
                typeof(INameResolver),
                typeof(IJobActivator),
                typeof(IExtensionTypeLocator),
                typeof(SingletonManager),
                typeof(IHostIdProvider),
                typeof(IQueueConfiguration),
                typeof(IExtensionRegistry),
                typeof(IDistributedLockManager),
                typeof(IFunctionIndexProvider) // set to unit test indexing. 
            };

            foreach (var obj in services)
            {
                if (obj == null)
                {
                    continue;
                }

                IStorageAccountProvider storageAccountProvider = obj as IStorageAccountProvider;
                IStorageAccount account = obj as IStorageAccount;
                if (account != null)
                {
                    storageAccountProvider = new FakeStorageAccountProvider
                    {
                        StorageAccount = account
                    };
                }
                if (storageAccountProvider != null)
                {
                    config.AddService<IStorageAccountProvider>(storageAccountProvider);
                    continue;
                }

                // A new extension 
                IExtensionConfigProvider extension = obj as IExtensionConfigProvider;
                if (extension != null)
                {
                    extensions.RegisterExtension<IExtensionConfigProvider>(extension);
                    continue;
                }

                // basic pattern. 
                bool ok = false;
                foreach (var type in types)
                {
                    if (type.IsAssignableFrom(obj.GetType()))
                    {
                        config.AddService(type, obj);
                        ok = true;
                        break;
                    }
                }
                if (ok)
                {
                    continue;
                }

                throw new InvalidOperationException("Test bug: Unrecognized type: " + obj.GetType().FullName);                
            }
        }

        private class FakeStorageAccountProvider : IStorageAccountProvider
        {
            public IStorageAccount StorageAccount { get; set; }

            public IStorageAccount DashboardAccount { get; set; }

            public Task<IStorageAccount> TryGetAccountAsync(string connectionStringName, CancellationToken cancellationToken)
            {
                IStorageAccount account;

                if (connectionStringName == ConnectionStringNames.Storage)
                {
                    account = StorageAccount;
                }
                else if (connectionStringName == ConnectionStringNames.Dashboard)
                {
                    account = DashboardAccount;
                }
                else
                {
                    account = null;
                }

                return Task.FromResult(account);
            }
        }

        public static IJobHostMetadataProvider CreateMetadataProvider(this JobHost host)
        {
            return host.Services.GetService<IJobHostMetadataProvider>();
        }
    }
}
