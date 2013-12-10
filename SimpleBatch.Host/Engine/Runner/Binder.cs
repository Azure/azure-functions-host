using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    public class BindingContext : IBinderEx, IBinderPrivate
    {
        private readonly IRuntimeBindingInputs _runtimeInputs;
        private readonly IConfiguration _config;
        private readonly FunctionInstanceGuid _FunctionInstanceGuid;
        private readonly INotifyNewBlob _notificationService;

        public BindingContext(IConfiguration config, IRuntimeBindingInputs runtimeInputs, FunctionInstanceGuid functionInstance, INotifyNewBlob notificationService)
        {
            _config = config;
            _runtimeInputs = runtimeInputs;
            _FunctionInstanceGuid = functionInstance;
            _notificationService = notificationService;
        }

        // optionally pass in names, which flow to RuntimeBindingInputs?
        // names would just resolve against {} tokens in attributes?
        public BindResult<T> Bind<T>(Attribute a)
        {
            // Always bind as input parameters, eg no 'out' keyword. 
            // The binding could still have output semantics. Eg, bind to a TextWriter. 
            ParameterInfo p = new FakeParameterInfo(typeof(T), name: "?", isOut: false);

            // Same static binding as used in indexing
            ParameterStaticBinding staticBind = StaticBinder.DoStaticBind(a, p); 

            // If somebody tried an non-sensical bind, we'd get the failure here 
            // here because the binding input doesn't have the information. 
            // Eg, eg a QueueInput attribute would fail because input doesn't have a queue input message.
            ParameterRuntimeBinding runtimeBind = staticBind.Bind(_runtimeInputs);

            BindResult result = runtimeBind.Bind(_config, this, p);
            return Utility.StrongWrapper<T>(result);                
        }
        
        public string AccountConnectionString
        {
            get { return _runtimeInputs.AccountConnectionString; }
        }


        public Guid FunctionInstanceGuid
        {
            get { return _FunctionInstanceGuid;  }
        }

        INotifyNewBlob IBinderPrivate.NotifyNewBlob
        {
            get { return _notificationService; }
        }
    }


    public class CollisionDetector
    {
        // ### We don't have the Static binders...
        // Throw if binds read and write to the same resource. 
        public static void DetectCollisions(BindResult[] binds)
        {
        }
    }
}