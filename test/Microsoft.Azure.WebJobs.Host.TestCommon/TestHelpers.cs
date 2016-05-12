using Microsoft.Azure.WebJobs.Host.Config;
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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

        // Helper to create a JobHostConfiguraiton from a set of services. 
        // Default config, pure-in-memory
        // Default is pure-in-memory, good for unit testing. 
        public static JobHostConfiguration NewConfig(
            Type functions, 
            params object[] services
            )
        {
            JobHostConfiguration config = new JobHostConfiguration()
            {
                TypeLocator = new FakeTypeLocator(functions),

                // Pure in-memory, no storage. 
                HostId = Guid.NewGuid().ToString("n"),
                DashboardConnectionString = null,
                StorageConnectionString = null
            };

            IExtensionRegistry extensions = config.GetService<IExtensionRegistry>();

            foreach (var obj in services)
            {
                INameResolver nameResolver = obj as INameResolver;
                if (nameResolver != null)
                {
                    config.NameResolver = nameResolver;
                    continue;
                }
                IJobActivator jobActivator = obj as IJobActivator;
                if (jobActivator != null)
                {
                    config.JobActivator = jobActivator;
                    continue;
                }

                IExtensionConfigProvider extension = obj as IExtensionConfigProvider;
                if (extension != null)
                {
                    extensions.RegisterExtension<IExtensionConfigProvider>(extension);
                    continue;
                }

                throw new InvalidOperationException("Test bug: Unrecognized type: " + obj.GetType().FullName);                
            }
            
            return config;
        }
    }
}
