using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Bindings.BinderProviders;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Used for launching an instance
    internal class RunnerProgram
    {
        private readonly TextWriter _consoleOutput;
        private readonly CloudBlobDescriptor _parameterLogger;

        public RunnerProgram(TextWriter consoleOutput, CloudBlobDescriptor parameterLogger)
        {
            _consoleOutput = consoleOutput;
            _parameterLogger = parameterLogger;
        }

        public static FunctionExecutionResult MainWorker(TextWriter consoleOutput, CloudBlobDescriptor parameterLogger,
            FunctionInvokeRequest descr, IConfiguration config, CancellationToken cancellationToken)
        {
            RunnerProgram program = new RunnerProgram(consoleOutput, parameterLogger);
            return program.MainWorker(descr, config, cancellationToken);
        }

        private FunctionExecutionResult MainWorker(FunctionInvokeRequest request, IConfiguration configuration,
            CancellationToken cancellationToken)
        {
            _consoleOutput.WriteLine("running in pid: {0}", System.Diagnostics.Process.GetCurrentProcess().Id);
            _consoleOutput.WriteLine("Timestamp:{0}", DateTime.Now.ToLongTimeString());

            FunctionExecutionResult result = new FunctionExecutionResult();

            try
            {
                Invoke(request, configuration, cancellationToken);

                // Success
                _consoleOutput.WriteLine("Success");
            }
            catch (Exception e)
            {
                // both binding errors and user exceptions from the function will land here. 
                result.ExceptionType = e.GetType().FullName;
                result.ExceptionMessage = e.Message;

                // Failure. 
                _consoleOutput.WriteLine("Exception while executing:");
                WriteExceptionChain(e, _consoleOutput);
                _consoleOutput.WriteLine("FAIL");
            }

            return result;
        }

        // Write an exception and inner exceptions
        public static void WriteExceptionChain(Exception e, TextWriter output)
        {
            Exception e2 = e;
            while (e2 != null)
            {
                output.WriteLine("{0}, {1}", e2.GetType().FullName, e2.Message);

                // Write bonus information for extra diagnostics
                var se = e2 as StorageException;
                if (se != null)
                {
                    var nvc = se.RequestInformation.ExtendedErrorInformation.AdditionalDetails;

                    foreach (var key in nvc.Keys)
                    {
                        output.WriteLine("  >{0}: {1}", key, nvc[key]);
                    }
                }

                output.WriteLine(e2.StackTrace);
                output.WriteLine();
                e2 = e2.InnerException;
            }
        }

        public void Invoke(FunctionInvokeRequest invoke, IConfiguration config, CancellationToken cancellationToken)
        {
            MethodInfo method = GetLocalMethod(invoke);
            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(invoke.Location);
            bool hasBindError;
            IReadOnlyDictionary<string, IValueProvider> parameters = CreateCombinedParameters(config, method, invoke.Id,
                inputs, invoke.Parameters, invoke.Args, cancellationToken, out hasBindError);
            Invoke(method, parameters, hasBindError);
        }

        private static MethodInfo GetLocalMethod(FunctionInvokeRequest invoke)
        {
            var methodLocation = invoke.Location as MethodInfoFunctionLocation;
            if (methodLocation != null)
            {
                var method = methodLocation.MethodInfo;
                if (method != null)
                {
                    return method;
                }
            }

            throw new InvalidOperationException("Can't get a MethodInfo from function location:" + invoke.Location.ToString());

        }

        public static IConfiguration InitBinders()
        {
            Configuration config = new Configuration();

            AddDefaultBinders(config);
            return config;

        }

        public static void AddDefaultBinders(IConfiguration config)
        {
            // Blobs
            config.BlobBinders.Add(new CloudBlockBlobBinderProvider());
            config.BlobBinders.Add(new CloudPageBlobBinderProvider());
            config.BlobBinders.Add(new CloudBlobBinderProvider());
            config.BlobBinders.Add(new BlobStreamBinderProvider());
            config.BlobBinders.Add(new TextWriterProvider());
            config.BlobBinders.Add(new StringBlobBinderProvider());

            // Tables
            config.TableBinders.Add(new CloudTableBinderProvider());
            config.TableBinders.Add(new QueryableCloudTableBinderProvider());
            config.TableBinders.Add(new DictionaryTableBinderProvider());
        }

        internal static bool ShouldIgnoreInvokeString(Type parameterType)
        {
            // Work around problem using IBinder and CloudStorageAccount with Run/Replay from dashboard.
            if (parameterType == typeof(IBinder))
            {
                return true;
            }
            else if (parameterType == typeof(CloudStorageAccount))
            {
                return true;
            }
            else if (parameterType.Namespace == "Microsoft.WindowsAzure.Storage" && parameterType.Name == "CloudStorageAccount")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static void ApplyHooks(Type t, IConfiguration config)
        {
            var methodInit = t.GetMethod("Initialize",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public, null,
                new Type[] { typeof(IConfiguration) }, null);
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

        private IReadOnlyDictionary<string, IValueProvider> CreateCombinedParameters(IConfiguration config, MethodInfo m, Guid instance,
            IRuntimeBindingInputs inputs, IReadOnlyDictionary<string, IValueProvider> parameters, ParameterRuntimeBinding[] runtimeBindings,
            CancellationToken cancellationToken, out bool hasBindError)
        {
            hasBindError = false;

            Dictionary<string, IValueProvider> combinedParameters = new Dictionary<string, IValueProvider>();

            INotifyNewBlob notificationService = new NotifyNewBlobViaInMemory();
            IBinderEx bindingContext = new BinderEx(config, inputs, instance, notificationService, _consoleOutput, cancellationToken);

            foreach (ParameterInfo parameterInfo in m.GetParameters())
            {
                string name = parameterInfo.Name;
                IValueProvider valueProvider = parameters != null && parameters.ContainsKey(name) ? parameters[name] : null;

                if (valueProvider != null)
                {
                    combinedParameters.Add(name, valueProvider);
                    continue;
                }

                ParameterRuntimeBinding runtimeBinding = runtimeBindings.SingleOrDefault(b => b.Name == name);

                if (valueProvider == null && runtimeBindings == null)
                {
                    throw new InvalidOperationException("No value provided for parameter '" + name + "'.");
                }

                BindResult result;

                try
                {
                    result = runtimeBinding.Bind(config, bindingContext, parameterInfo);
                }
                catch (Exception exception)
                {
                    string msg = String.Format(CultureInfo.InvariantCulture, "Error while binding parameter {0} '{1}':{2}",
                        name, parameterInfo, exception.Message);
                    result = new NullBindResult(msg);
                    hasBindError = true;
                }

                combinedParameters.Add(name, new BindResultValueProvider(result, parameterInfo.ParameterType));
            }

            return combinedParameters;
        }

        private void Invoke(MethodInfo m, IReadOnlyDictionary<string, IValueProvider> parameters, bool hasBindError)
        {
            ParameterInfo[] parameterInfos = m.GetParameters();
            int length = parameterInfos.Length;
            object[] arguments = new object[length];
            ISelfWatch[] watches = new ISelfWatch[length];

            for (int index = 0; index < length; index++)
            {
                string name = parameterInfos[index].Name;
                IValueProvider valueProvider = parameters[name];
                arguments[index] = valueProvider.GetValue();

                IWatchable watchable = valueProvider as IWatchable;
                ISelfWatch watcher;

                if (watchable != null)
                {
                    watcher = watchable.Watcher;
                }
                else
                {
                    watcher = null;
                }

                watches[index] = watcher;
            }

            _consoleOutput.WriteLine("Parameters bound. Invoking user function.");
            _consoleOutput.WriteLine("--------");

            SelfWatch fpStopWatcher = null;
            try
            {
                fpStopWatcher = InvokeWorker(m, arguments, watches, hasBindError);
            }
            finally
            {
                // Process any out parameters, do any cleanup
                // For update, do any cleanup work. 

                // Ensure IValueBinder.SetValue is called in BindOrder. This ordering is particularly important for
                // ensuring queue outputs occur last. That way, all other function side-effects are guaranteed to have
                // occurred by the time messages are enqueued.
                string[] parameterNamesInBindOrder = SortParameterNamesInStepOrder(parameters);

                try
                {
                    _consoleOutput.WriteLine("--------");

                    foreach (string name in parameterNamesInBindOrder)
                    {
                        IValueProvider provider = parameters[name];
                        IValueBinder binder = provider as IValueBinder;
                        IDisposable disposable = provider as IDisposable;
                        object argument = arguments[GetParameterIndex(parameterInfos, name)];

                        try
                        {
                            if (binder != null)
                            {
                                // This could invoke do complex things that may fail. Catch the exception.
                                binder.SetValue(argument);
                            }

                            if (disposable != null)
                            {
                                // Due to BindResult adapter, this could also fail.
                                disposable.Dispose();
                            }
                        }
                        catch (Exception e)
                        {
                            string msg = string.Format("Error while handling parameter {0} '{1}' after function returned:", name, argument);
                            throw new InvalidOperationException(msg, e);
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

        private static int GetParameterIndex(ParameterInfo[] parameters, string name)
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].Name == name)
                {
                    return index;
                }
            }

            throw new InvalidOperationException("Cannot find parameter + " + name + ".");
        }

        private static string[] SortParameterNamesInStepOrder(IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            string[] parameterNames = new string[parameters.Count];
            int index = 0;

            foreach (string parameterName in parameters.Keys)
            {
                parameterNames[index] = parameterName;
                index++;
            }

            IValueProvider[] parameterValues = new IValueProvider[parameters.Count];
            index = 0;

            foreach (IValueProvider parameterValue in parameters.Values)
            {
                parameterValues[index] = parameterValue;
                index++;
            }

            Array.Sort(parameterValues, parameterNames, ValueBinderStepOrderComparer.Instance);
            return parameterNames;
        }

        private SelfWatch InvokeWorker(MethodInfo m, object[] arguments, ISelfWatch[] watches, bool hasBindError)
        {
            SelfWatch fpStopWatcher = null;
            if (_parameterLogger != null)
            {
                CloudBlockBlob blobResults = _parameterLogger.GetBlockBlob();
                fpStopWatcher = new SelfWatch(watches, blobResults, _consoleOutput);
            }

            try
            {
                if (!hasBindError)
                {
                    if (IsAsyncMethod(m))
                    {
                        InformNoAsyncSupport();
                    }

                    object returnValue = m.Invoke(null, arguments);
                    HandleFunctionReturnParameter(m, returnValue);
                }
                else
                {
                    throw new InvalidOperationException("Error while binding function parameters.");
                }
            }
            catch (TargetInvocationException e)
            {
                // $$$ Beware, this loses the stack trace from the user's invocation
                // Print stacktrace to console now while we have it.
                _consoleOutput.WriteLine(e.InnerException.StackTrace);

                throw e.InnerException;
            }

            return fpStopWatcher;
        }

        /// <summary>
        /// Handles the function return value and logs it, if necessary
        /// </summary>
        private void HandleFunctionReturnParameter(MethodInfo m, object returnValue)
        {
            Type returnType = m.ReturnType;

            if (returnType == typeof(void))
            {
                // No need to do anything
                return;
            }
            else if (IsAsyncMethod(m))
            {
                Task t = returnValue as Task;
                t.Wait();

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    PropertyInfo resultProperty = returnType.GetProperty("Result");
                    object result = resultProperty.GetValue(returnValue);

                    LogReturnValue(result);
                }
            }
            else
            {
                LogReturnValue(returnValue);
            }
        }

        private static bool IsAsyncMethod(MethodInfo m)
        {
            Type returnType = m.ReturnType;

            return typeof(Task).IsAssignableFrom(returnType);
        }

        private void InformNoAsyncSupport()
        {
            _consoleOutput.WriteLine("Warning: This asynchronous method will be run synchronously.");
        }

        private void LogReturnValue(object value)
        {
            _consoleOutput.WriteLine("Return value: {0}", value != null ? value.ToString() : "<null>");
        }

        private class ValueBinderStepOrderComparer : IComparer<IValueProvider>
        {
            private static readonly ValueBinderStepOrderComparer _instance = new ValueBinderStepOrderComparer();

            private ValueBinderStepOrderComparer()
            {
            }

            public static ValueBinderStepOrderComparer Instance { get { return _instance; } }

            public int Compare(IValueProvider x, IValueProvider y)
            {
                int xOrder = GetStepOrder(x);
                int yOrder = GetStepOrder(y);

                return Comparer<int>.Default.Compare(xOrder, yOrder);
            }

            private static int GetStepOrder(IValueProvider provider)
            {
                IOrderedValueBinder orderedBinder = provider as IOrderedValueBinder;

                if (orderedBinder == null)
                {
                    return BindStepOrders.Default;
                }

                return orderedBinder.StepOrder;
            }
        }

        private class BindResultValueProvider : IValueBinder, IWatchable, IDisposable
        {
            private readonly BindResult _bindResult;
            private readonly ISelfWatch _watcher;

            private bool _disposed;

            public BindResultValueProvider(BindResult bindResult, Type parameterType)
            {
                _bindResult = bindResult;
                _watcher = SelfWatch.GetWatcher(bindResult, parameterType);
            }

            public Type Type
            {
                get { throw new NotSupportedException(); }
            }

            public ISelfWatch Watcher
            {
                get { return _watcher; }
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _bindResult.OnPostAction();
                    _disposed = true;
                }
            }

            public object GetValue()
            {
                return _bindResult.Result;
            }

            public void SetValue(object value)
            {
                _bindResult.Result = value;
            }

            public string ToInvokeString()
            {
                throw new NotSupportedException();
            }
        }
    }
}
