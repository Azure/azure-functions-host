using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace AssemblyLoadContextRace
{
    public static class Function1
    {
        [FunctionName("Function1")]
        public static IActionResult Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequest req)
        {
            try
            {
                var resetEvent = new ManualResetEvent(false);

                // First, force Newtonsoft to load on many threads, which induces the race
                var context = AssemblyLoadContext.GetLoadContext(typeof(Function1).Assembly);

                void LoadType()
                {
                    var assembly = context.LoadFromAssemblyName(new AssemblyName("Newtonsoft.Json, Version=12.0.3.0"));
                }

                List<Thread> threads = new List<Thread>();

                for (int i = 0; i < 100; i++)
                {
                    Thread t = new Thread(LoadType);
                    threads.Add(t);
                    t.Start();
                }

                foreach (Thread t in threads)
                {
                    t.Join();
                }

                // Now, make sure the assemblies match, signifying that the race was fixed and we 
                // always load the host's version.
                var functionAssembly = context.LoadFromAssemblyName(new AssemblyName("Newtonsoft.Json, Version=12.0.3.0"));
                var defaultAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("Newtonsoft.Json, Version=12.0.3.0"));

                if (!Equals(functionAssembly, defaultAssembly))
                {
                    throw new InvalidOperationException("The FunctionAssemblyLoadContext Newtonsoft.Json assembly is not the same as the default AssemblyLoadContext assembly.");
                }
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex.ToString())
                {
                    StatusCode = 500
                };
            }

            return new OkResult();
        }
    }
}
