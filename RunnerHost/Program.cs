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
    // Used for launching an instance
    public class Program
    {
        public static void Main(string[] args)
        {
            var x = System.Diagnostics.Stopwatch.StartNew();
            var s = x.ToString();

            var client = new Utility.ProcessExecuteArgs<LocalFunctionInstance, FunctionExecutionResult>(args);

            LocalFunctionInstance descr = client.Input;
            var result = MainWorker(descr);

            client.Result = result;
        }
        
        private static FunctionExecutionResult MainWorker(LocalFunctionInstance descr)
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

                Exception e2 = e;
                while (e2 != null)
                {
                    Console.WriteLine(e2.Message);
                    Console.WriteLine(e2.StackTrace);
                    Console.WriteLine();
                    e2 = e2.InnerException;
                }
                Console.WriteLine("FAIL");
            }

            return result;
        }

        public static void Invoke(LocalFunctionInstance invoke)
        {
            MethodInfo method = GetLocalMethod(invoke);

            // GEt standard config. 
            // Use an ICall that binds against the WebService provided by the local function instance.
            IConfiguration config = InitBinders();
            ApplyHooks(method, config);
            CallBinderProvider.Insert(() => GetWebInvoker(invoke), config);

            Invoke(config, method, invoke.Args);
        }

        static FunctionInvoker GetWebInvoker(LocalFunctionInstance instance)
        {
            string url = instance.ServiceUrl;

            // Scope = caller's scope minus the method name at the end.
            string scope = instance.Location.GetId();
            int len = instance.Location.MethodName.Length;
            scope = scope.Substring(0, scope.Length - len - 1);

            var result = new WebFunctionInvoker(scope, url);

            return result;
        }

        private static MethodInfo GetLocalMethod(LocalFunctionInstance invoke)
        {
            Assembly a = Assembly.LoadFrom(invoke.AssemblyPath);
            Type t = a.GetType(invoke.TypeName);
            if (t == null)
            {
                throw new InvalidOperationException(string.Format("Type '{0}' does not exist.", invoke.TypeName));
            }

            MethodInfo m = t.GetMethod(invoke.MethodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null)
            {
                throw new InvalidOperationException(string.Format("Method '{0}' does not exist.", invoke.MethodName));
            }
            return m;
        }

        // $$$ get rid of static fields.
        static CloudBlobDescriptor _parameterLogger;

        // Query the selfwatches and update the live parameter info.
        static void LogSelfWatch(ISelfWatch[] watches, CloudBlob paramBlob)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var watch in watches)
            {
                if (watch != null)
                {
                    string val = watch.GetStatus();
                    sb.AppendLine(val);
                }
                else
                {
                    sb.AppendLine(); // blank for a place holder.
                }
            }
            try
            {
                paramBlob.UploadText(sb.ToString());
            }
            catch
            {
                // Not fatal if we can't update selfwatch. 
                // Could happen because we're calling on a timer, and so it 
                // could get invoked concurrently on multiple threads, which 
                // could contend over writing.
            }
        }

        // Begin self-watches. Return a cleanup delegate for stopping the watches. 
        // May update args array with selfwatch wrappers.
        static Action StartSelfWatcher(BindResult[] binds, ParameterInfo[] ps)
        {
            if (_parameterLogger == null)
            {
                // Can't self-watch, no where to log to
                return null;
            }
            // Initial Self watchers on the parameters.
            var paramBlob = _parameterLogger.GetBlob();

            int len = binds.Length;
            ISelfWatch[] watches = new ISelfWatch[len];
            for(int i =0; i < len; i++)
            {
                watches[i] = GetWatcher(binds[i], ps[i]);
            }

            var refreshRate = TimeSpan.FromSeconds(3);

            TimerCallback callback = obj =>
            {
                if (_parameterLogger == null)
                {
                    return;
                }
                LogSelfWatch(watches, paramBlob);
            };

            // Given an initial quick update so that user sees non-zero values for self-watch.
            Timer timer = new Timer(callback, null, TimeSpan.FromSeconds(3), refreshRate);

            // Deferred function to stop the self-watching on parameters.
            Action fpStopWatcher = () =>
            {
                timer.Dispose();
                _parameterLogger = null;

                // Flush remaining. do this after timer has been shutdown to avoid races. 
                LogSelfWatch(watches, paramBlob);
            };
            return fpStopWatcher;
        }


        public static IConfiguration InitBinders()
        {            
            Configuration config = new Configuration();

            // Blobs
            config.BlobBinders.Add(new EnumerableBlobBinderProvider());
            config.BlobBinders.Add(new BlobStreamBinderProvider());
            config.BlobBinders.Add(new TextReaderProvider());
            config.BlobBinders.Add(new TextWriterProvider());
            
            // Tables
            config.TableBinders.Add(new TableBinderProvider());
            config.TableBinders.Add(new StrongTableBinderProvider());
            config.TableBinders.Add(new DictionaryTableBinderProvider());

            // Other
            config.Binders.Add(new QueueOutputProvider());

            config.Binders.Add(new BinderBinderProvider()); // for IBinder

            return config;
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
                    methodInit.Invoke(null, new object[] { config });
                }
            }
        }

        // ###
        // Get this from the LocalFunctionInstance instead.
        private static string GetAccountString(ParameterRuntimeBinding[] argDescriptors)
        {
            foreach (var param in argDescriptors)
            {
                var blob = param as BlobParameterRuntimeBinding;
                if (blob != null)
                {
                    return blob.Blob.AccountConnectionString;
                }

                var table = param as TableParameterRuntimeBinding;
                if (table != null)
                {
                    return table.Table.AccountConnectionString;
                }
            }

            return null; // unknown account
        }

        public static void Invoke(IConfiguration config, MethodInfo m, ParameterRuntimeBinding[] argDescriptors)
        {
            int len = argDescriptors.Length;

            string accountConnectionString = GetAccountString(argDescriptors);
            IBinder bindingContext = new BindingContext(config, accountConnectionString);

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
            try
            {
                InvokeWorker(m, binds, ps);
                success = true;
            }
            finally
            {
                Console.WriteLine("--------");

                // Process any out parameters, do any cleanup
                // For update, do any cleanup work. 
                foreach(var bind in binds)
                {
                    bind.OnPostAction();
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
        }

        public static void InvokeWorker(MethodInfo m, BindResult[] binds, ParameterInfo[] ps)
        {
            Action fpStopWatcher = StartSelfWatcher(binds, ps);

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

                if (fpStopWatcher != null)
                {
                    fpStopWatcher();
                }             
            }
        }

        // May update the object with a Selfwatch wrapper.
        static ISelfWatch GetWatcher(BindResult bind, ParameterInfo targetParameter)
        {
            return GetWatcher(bind, targetParameter.ParameterType);
        }

        public static ISelfWatch GetWatcher(BindResult bind, Type targetType)
        {
            ISelfWatch watch = bind.Watcher;
            if (watch != null)
            { 
                // If explicitly provided, use that.
                return watch;
            }

            watch = bind.Result as ISelfWatch;
            if (watch != null)
            {
                return watch;
            }

            // See if we can apply a watcher on the result
            var t = IsIEnumerableT(targetType);
            if (t != null)
            {
                var tWatcher = typeof(WatchableEnumerable<>).MakeGenericType(t);
                var result = Activator.CreateInstance(tWatcher, bind.Result);

                bind.Result = result; // Update to watchable version.
                return result as ISelfWatch;
            }

            // Nope, 
            return null;
        }        

        // Get the T from an IEnumerable<T>. 
        internal static Type IsIEnumerableT(Type typeTarget)
        {
            if (typeTarget.IsGenericType)
            {
                var t2 = typeTarget.GetGenericTypeDefinition();
                if (t2 == typeof(IEnumerable<>))
                {
                    // RowAs<T> doesn't take System.Type, so need to use some reflection. 
                    var rowType = typeTarget.GetGenericArguments()[0];
                    return rowType;
                }
            }
            return null;
        }        
    }        
}
