using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;
using SimpleBatch.Client;

namespace RunnerHost
{
    // Helper to redirect std.out if this function is launched as an appdomain.
    // This a hook that can be invoked by whoever creates the appdomain.
    // See:
    // http://blogs.artinsoft.net/mrojas/archive/2008/10/02/outofprocess-in-c.aspx
    public class OutputSetter : MarshalByRefObject
    {
        public OutputSetter()
        {
        }
        public void SetOut(TextWriter output)
        {
            Console.SetOut(output);
        }
    }

    // Used for launching an instance
    public class Program
    {
        public static void Main(string[] args)
        {
            var client = new Utility.ProcessExecuteArgs<FunctionInvokeRequest, FunctionExecutionResult>(args);

            FunctionInvokeRequest descr = client.Input;
            var result = MainWorker(descr);

            client.Result = result;
        }

        public static FunctionExecutionResult MainWorker(FunctionInvokeRequest descr)
        {                       
            Console.WriteLine("running in pid: {0}", System.Diagnostics.Process.GetCurrentProcess().Id);
            Console.WriteLine("Timestamp:{0}", DateTime.Now.ToLongTimeString());
         
            _parameterLogger = descr.ParameterLogBlob; // optional 

            FunctionExecutionResult result = new FunctionExecutionResult();

            try
            {
                Invoke(descr);
                // Success
                Console.WriteLine("Success");
            }
            catch(Exception e)
            {
                // both binding errors and user exceptions from the function will land here. 
                result.ExceptionType = e.GetType().FullName;
                result.ExceptionMessage = e.Message;

                // Failure. 
                Console.WriteLine("Exception while executing:");
                WriteExceptionChain(e);                
                Console.WriteLine("FAIL");
            }

            return result;
        }

        // Write an exception and inner exceptions
        private static void WriteExceptionChain(Exception e)
        {
            Exception e2 = e;
            while (e2 != null)
            {
                Console.WriteLine("{0}, {1}", e2.GetType().FullName, e2.Message);

                // Write bonus information for extra diagnostics
                var se = e2 as StorageClientException;
                if (se != null)
                {
                    var nvc = se.ExtendedErrorInformation.AdditionalDetails;                  

                    foreach (var key in nvc.AllKeys)
                    {
                        Console.WriteLine("  >{0}: {1}", key, nvc[key]);
                    }
                }

                Console.WriteLine(e2.StackTrace);
                Console.WriteLine();
                e2 = e2.InnerException;
            }
        }

        public static void Invoke(FunctionInvokeRequest invoke, IConfiguration config)
        {
            MethodInfo method = GetLocalMethod(invoke);
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(invoke.Location);
            Invoke(config, method, invoke.Id, invoke.ServiceUrl, inputs, invoke.Args);
        }

        public static void Invoke(FunctionInvokeRequest invoke)
        {
            MethodInfo method = GetLocalMethod(invoke);

            // Get standard config. 
            // Use an ICall that binds against the WebService provided by the local function instance.
            IConfiguration config = InitBinders();
            ApplyManifestBinders(invoke, config);
            ApplyHooks(method, config); // Give user hooks higher priority than any cloud binders

            ICall inner = GetWebInvoker(invoke);
            CallBinderProvider.Insert(config, inner); // binds ICall

            Invoke(invoke, config);
        }

        // Manifests are a way of adding binders to the configuration. 
        static void ApplyManifestBinders(FunctionInvokeRequest invoke, IConfiguration config)
        {
            var localLoc = invoke.Location as LocalFunctionLocation;
            if (localLoc == null)
            {
                // $$$ Assumes manifest only exists on disk. What about other types?
                return;
            }
            // Is there a manifest file?
            string path = Path.GetDirectoryName(localLoc.AssemblyPath);
            string file = Path.Combine(path, "manifest.txt"); 
            if (!File.Exists(file))
            {
                return;
            }
            string json = File.ReadAllText(file);
            var manifest  = JsonConvert.DeserializeObject<ModelBinderManifest>(json);

            ApplyManifestBinders(manifest, path, config);
        }

        // Path is the local path that the model binder assemblies are relative too. 
        static void ApplyManifestBinders(ModelBinderManifest manifest, string path, IConfiguration config)
        {
            foreach (var entry in manifest.Entries)
            {
                var assembly = Assembly.LoadFrom(Path.Combine(path, entry.AssemblyName));
                var t = assembly.GetType(entry.TypeName);
                if (t == null)
                {
                    throw new InvalidOperationException(string.Format("Type '{0}' does not exist.", entry.TypeName));
                }

                ApplyHooks(t, config);
            }
        }

        static ICall GetWebInvoker(FunctionInvokeRequest instance)
        {
            string url = instance.ServiceUrl;

            Func<string, string> functionResolver = (shortName) =>
                {
                    var newLoc = instance.Location.ResolveFunctionLocation(shortName);
                    var functionId = newLoc.ToString(); // Used with IFunctionTableLookup.
                    return functionId;
                };
            var result = new WebFunctionInvoker(functionResolver, url);

            return new WebCallWrapper(result);
        }

        private static MethodInfo GetLocalMethod(FunctionInvokeRequest invoke)
        {
            // For a RemoteFunctionLocation, we could download it and invoke. But assuming caller already did that. 
            // (Caller can cache the downloads and so do it more efficiently)
            var localLocation = invoke.Location as LocalFunctionLocation;
            if (localLocation != null)
            {
                return localLocation.GetLocalMethod();
            }

            var methodLocation = invoke.Location as MethodInfoFunctionLocation;
            if (methodLocation != null)
            {
                return methodLocation.MethodInfo;
            }

            throw new InvalidOperationException("Can't get a MethodInfo from function location:" + invoke.Location.ToString());
            
        }

        // $$$ get rid of static fields.
        static CloudBlobDescriptor _parameterLogger;
        
        public static IConfiguration InitBinders()
        {            
            Configuration config = new Configuration();

            AddDefaultBinders(config);
            return config;

        }
        public static void AddDefaultBinders(IConfiguration config)
        {
            // Blobs
            config.BlobBinders.Add(new CloudBlobBinderProvider());
            config.BlobBinders.Add(new BlobStreamBinderProvider());
            config.BlobBinders.Add(new TextReaderProvider());
            config.BlobBinders.Add(new TextWriterProvider());
            
            // Tables
            config.TableBinders.Add(new TableBinderProvider());
            config.TableBinders.Add(new StrongTableBinderProvider());
            config.TableBinders.Add(new DictionaryTableBinderProvider());

            // Other
            config.Binders.Add(new QueueOutputBinderProvider());
            config.Binders.Add(new CloudStorageAccountBinderProvider());

            config.Binders.Add(new BinderBinderProvider()); // for IBinder
            config.Binders.Add(new ContextBinderProvider()); // for IContext

            // Hook in optional binders for Azure 2.0 data types. 
            Azure20SdkBinders.Initialize.Add(config);
        }

        private static void ApplyHooks(MethodInfo method, IConfiguration config)
        {
            // Find a hook based on the MethodInfo, and if found, invoke the config
            // Look for Initialize(IConfiguration c) in the same type?

            var t = method.DeclaringType;
            ApplyHooks(t, config);
        }
        
        public static void ApplyHooks(Type t, IConfiguration config)
        {
            var methodInit = t.GetMethod("Initialize", new Type[] { typeof(IConfiguration) } );
            if (methodInit != null)
            {
                if (methodInit.IsStatic && methodInit.IsPublic)
                {
                    try
                    {
                        methodInit.Invoke(null, new object[] { config });
                    }
                    catch (TargetInvocationException ex)
                    {
                        // This will lose original callstack. Hopefully message is complete enough. 
                        if (ex.InnerException is InvalidOperationException)
                        {
                            throw ex.InnerException;
                        }
                    }
                }
            }
        }

        // Have to still pass in IRuntimeBindingInputs since methods can do binding at runtime. 
        private static void Invoke(IConfiguration config, MethodInfo m, FunctionInstanceGuid instance, string serviceUrl, IRuntimeBindingInputs inputs, ParameterRuntimeBinding[] argDescriptors)
        {
            int len = argDescriptors.Length;

            IBinderEx bindingContext = new BindingContext(config, inputs, instance, serviceUrl);

            BindResult[] binds = new BindResult[len];
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < len; i++)
            {
                var p = ps[i];
                try
                {
                    binds[i] = argDescriptors[i].Bind(config, bindingContext, p);
                }
                catch (Exception e)
                {
                    string msg = string.Format("Error while binding parameter #{0} '{1}':{2}", i, p, e.Message);
                    throw new InvalidOperationException(msg, e);
                }
            }

            bool success = false;
            Console.WriteLine("Parameters bound. Invoking user function.");
            Console.WriteLine("--------");

            SelfWatch fpStopWatcher = null;
            try
            {
                fpStopWatcher = InvokeWorker(m, binds, ps);
                success = true;
            }
            finally
            {
                // Process any out parameters, do any cleanup
                // For update, do any cleanup work. 

                try
                {
                    Console.WriteLine("--------");

                    for (int i = 0; i < len; i++)
                    {
                        var bind = binds[i];
                        try
                        {
                            // This could invoke user code and do complex things that may fail. Catch the exception 
                            bind.OnPostAction();
                        }
                        catch (Exception e)
                        {
                            // This 
                            string msg = string.Format("Error while handling parameter #{0} '{1}' after function returned:", i, ps[i]);
                            throw new InvalidOperationException(msg, e);
                        }
                    }

                    if (success)
                    {
                        foreach (var bind in binds)
                        {
                            var a = bind as IPostActionTransaction;
                            if (a != null)
                            {
                                a.OnSuccessAction();
                            }
                        }
                    }

                }
                finally
                {
                    // Stop the watches last. PostActions may do things that should show up in the watches.
                    // PostActions could also take a long time (flushing large caches), and so it's useful to have
                    // watches still running.                
                    if (fpStopWatcher != null)
                    {
                        fpStopWatcher.Stop();
                    }
                }
            }            
        }

        public static SelfWatch InvokeWorker(MethodInfo m, BindResult[] binds, ParameterInfo[] ps)
        {
            SelfWatch fpStopWatcher  = null;
            if (_parameterLogger != null)
            {
                CloudBlob blobResults = _parameterLogger.GetBlob();
                fpStopWatcher = new SelfWatch(binds, ps, blobResults);
            }

            // Watchers may tweak args, so do those second.
            object[] args = Array.ConvertAll(binds, bind => bind.Result);

            try
            {
                m.Invoke(null, args);
            }
            catch (TargetInvocationException e)
            {
                // $$$ Beware, this loses the stack trace from the user's invocation
                // Print stacktrace to console now while we have it.
                Console.WriteLine(e.InnerException.StackTrace);                

                throw e.InnerException;
            }
            finally
            {
                // Copy back any ref/out parameters
                for (int i = 0; i < binds.Length; i++)
                {
                    binds[i].Result = args[i];
                }       
            }

            return fpStopWatcher;
        }        
    }        
}
