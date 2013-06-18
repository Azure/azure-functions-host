using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    public class UnknownParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string AccountConnectionString { get; set; }
        public override string ConvertToInvokeString()
        {
            return string.Empty;
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            ICloudBinder binder = GetBinderOrThrow(config, targetParameter);

            // We want to preserve the same binding context to maximize consistency between model bound parameters 
            // and explictly calling IBinder. 
            // Sanity check that they're using the same accounts. 
            if (this.AccountConnectionString != bindingContext.AccountConnectionString)
            {
                var name1 = Utility.GetAccountName(this.AccountConnectionString);
                var name2 = Utility.GetAccountName(bindingContext.AccountConnectionString);

                string msg = string.Format("Binding has conflicting accounts: {0} vs {1}", name1, name2);
                throw new InvalidOperationException(msg);
            }
            return binder.Bind(bindingContext, targetParameter);
        }

        public static ICloudBinder GetBinderOrThrow(IConfiguration config, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            ICloudBinder binder = config.GetBinder(t);
            if (binder == null)
            {
                string msg = string.Format("Can't bind parameter '{0}' to type '{1}'. Are you missing a custom model binder or binding attribute ([Blob], [Queue], [Table])?", targetParameter.Name, t);
                throw new InvalidOperationException(msg);
            }
            return binder;
        }
    }
}