using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Orchestrator;
using RunnerInterfaces;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using System.Threading;

namespace SimpleBatch
{
    // Public interface for an app to host SimpleBatch
    // and invoke simple batch functions. 
    public class Host
    {
        // Where we log things to. 
        // Null if logging is not supported (this is required for pumping).        
        private readonly string _loggingAccountConnectionString;

        // The user account that we listen on.
        // This is the account that the bindings resolve against.
        private readonly string _userAccountConnectionString;

        private HostContext _ctx;

        // Get from app settings 
        public Host() : this(null, null)
        {            
        }

        // Ctor for when the User account and Logging account go to the same thing. 
        public Host(string userAndLoggingAccountConnectionString)
            : this (userAndLoggingAccountConnectionString, userAndLoggingAccountConnectionString)
        {
        }

        public Host(string userAccountConnectionString, string loggingAccountConnectionString)
        {
            _loggingAccountConnectionString = GetSetting(loggingAccountConnectionString, "SimpleBatchLoggingACS");
            _userAccountConnectionString = GetSetting(userAccountConnectionString, "SimpleBatchUserACS");

            Validate();

            if (_loggingAccountConnectionString != null)
            {
                _ctx = GetHostContext();
            }
        }

        public string UserAccountName
        {
            get { return Utility.GetAccountName(_userAccountConnectionString); }
        }

        private static string GetSetting(string overrideValue, string settingName)
        {
            return overrideValue ?? ConfigurationManager.AppSettings[settingName];
        }

        void Validate()
        {
            if (_userAccountConnectionString == null)
            {
                throw new InvalidOperationException("User account connections tring is missing. This can be set via the 'SimpleBatchUserACS' appsetting or via the constructor.");
            }
            ValidateConnectionString(_userAccountConnectionString);
            if (_loggingAccountConnectionString != null)
            {
                if (_loggingAccountConnectionString != _userAccountConnectionString)
                {
                    ValidateConnectionString(_loggingAccountConnectionString);
                }
            }
        }

        // Throw if the account string is bad (parser error, wrong credentials). 
        static void ValidateConnectionString(string accountConnectionString)
        {
            // Will throw on parser errors. 
            CloudStorageAccount account;
            if (!CloudStorageAccount.TryParse(accountConnectionString, out account))            
            {
                throw new InvalidOperationException("Account connection string is an invalid format");
            }

            // Verify the credentials are correct.
            // Have to actually ping a storage operation. 
            try
            {
                var client = account.CreateCloudBlobClient();

                // This can hang for a long time if the account name is wrong. 
                // If will fail fast if the password is incorrect.
                client.GetServiceProperties();

                // Success
            }
            catch
            {
                string msg = string.Format("The account credentials for '{0}' are inccorect.", account.Credentials.AccountName);
                throw new InvalidOperationException(msg);
            }            
        }

        HostContext GetHostContext()
        {
            var ctx = new HostContext(_userAccountConnectionString, _loggingAccountConnectionString);
            return ctx;
        }

        // Run and return immediately 
        // This will spin up a background thread to listen and execute functions. 
        public void RunOnBackgroundThread()
        {
            RunOnBackgroundThread(CancellationToken.None);
        }

        // The thread exits when the cancellation token is signalled. 
        public void RunOnBackgroundThread(CancellationToken token)
        {
            Thread t = new Thread(_ => RunAndBlock(token));
            t.Start();
        }

        // Run the message loop.
        // Listen on triggers
        // This will scan the functions in the process.
        public void RunAndBlock()
        {
            RunAndBlock(CancellationToken.None);
        }
                        
        public void RunAndBlock(CancellationToken token)
        {
            //INotifyNewBlobListener fastpathNotify = new NotifyNewBlobViaQueueMessage(Utility.GetAccount(_loggingAccountConnectionString));
            INotifyNewBlobListener fastpathNotify = new NotifyNewBlobViaInMemory();

            using (Worker worker = new Worker(_ctx._functionTableLookup, _ctx._queueFunction, fastpathNotify))
            {
                while (!token.IsCancellationRequested)
                {
                    bool handled;
                    do
                    {
                        worker.Poll(token);
                        handled = HandleExecutionQueue(token);
                    }
                    while (handled);

                    Thread.Sleep(2 * 1000);
                    Console.Write(".");
                }
            }
        }

        private bool HandleExecutionQueue(CancellationToken token)
        {
            if (_ctx._executionQueue != null)
            {
                try
                {
                    bool handled = Utility.ApplyToQueue<FunctionInvokeRequest>(request => HandleFromExecutionQueue(request), _ctx._executionQueue);
                    return handled;
                }
                catch
                {
                    // Poision message. 
                }
            }
            return false;
        }

        private void HandleFromExecutionQueue(FunctionInvokeRequest request)
        {
            // Function was already queued (from the dashboard). So now we just need to activate it.
             //_ctx._queueFunction.Queue(request);
            IActivateFunction activate = (IActivateFunction) _ctx._queueFunction; // ### Make safe. 
            activate.ActivateFunction(request.Id);
        }
      
        // Invoke a single function 
        // arguments can be an anonymous object of an IDictionary
        public void Call(MethodInfo method, object arguments = null)
        {
            if (method == null)
            {
                throw new ArgumentNullException("Method");
            }

            if (_loggingAccountConnectionString != null)
            {
                CallWithLogging(method, arguments);
            }
            else
            {
                CallNoLogging(method, arguments);
            }
        }

        // Invoke the function via the SimpleBatch binders. 
        // All invoke is in-memory. Don't log anything to azure storage / function dashboard.
        private void CallNoLogging(MethodInfo method, object arguments = null)
        {
            // This creates with in-memory logging. Create against Azure logging. 
            var lc = new LocalExecutionContext(_userAccountConnectionString, method.DeclaringType);

            var guid = lc.Call(method, arguments);

            // If function fails, this should throw
            var lookup = lc.FunctionInstanceLookup;
            var logItem = lookup.LookupOrThrow(guid);

            VerifySuccess(logItem);
        }
        
        // Invoke the function via the SimpleBatch binders. 
        // Function execution is logged and viewable via the function dashboard.
        private void CallWithLogging(MethodInfo method, object arguments = null)
        {
            IDictionary<string, string> args2 = ObjectBinderHelpers.ConvertObjectToDict(arguments);

            FunctionDefinition func = ResolveFunctionDefinition(method, _ctx._functionTableLookup);
            FunctionInvokeRequest instance = Worker.GetFunctionInvocation(func, args2, null);

            instance.TriggerReason = new InvokeTriggerReason
            {
                Message = string.Format("This was function was programmatically called via the host APIs.")
            };

            var logItem = _ctx._queueFunction.Queue(instance);

            VerifySuccess(logItem);
        }

        // Throw if the function failed. 
        private static void VerifySuccess(ExecutionInstanceLogEntity logItem)
        {
            if (logItem.GetStatus() == FunctionInstanceStatus.CompletedFailed)
            {
                throw new Exception("Function failed:" + logItem.ExceptionMessage);
            }
        }

        private FunctionDefinition ResolveFunctionDefinition(MethodInfo method, IFunctionTableLookup functionTableLookup)
        {
            foreach (FunctionDefinition func in functionTableLookup.ReadAll())
            {
                MethodInfoFunctionLocation loc = func.Location as MethodInfoFunctionLocation;
                if (loc != null)
                {
                    if (loc.MethodInfo.Equals(method))
                    {
                        return func;
                    }
                }
            }

            string msg = string.Format("'{0}' can't be invoked from simplebatch. Is it missing simple batch bindings?", method);
            throw new InvalidOperationException(msg);
        }        
    }
}
