using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs
{
    internal class UnknownParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public string AccountConnectionString { get; set; }

        public override string ConvertToInvokeString()
        {
            return String.Empty;
        }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            ICloudBinder binder = GetBinderOrThrow(config, targetParameter);
            return Bind(binder, AccountConnectionString, bindingContext, targetParameter);
        }

        internal static BindResult Bind(ICloudBinder binder, string accountConnectionString, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            // We want to preserve the same binding context to maximize consistency between model bound parameters 
            // and explictly calling IBinder. 
            // Sanity check that they're using the same accounts. 
            if (accountConnectionString != bindingContext.AccountConnectionString)
            {
                var name1 = Utility.GetAccountName(accountConnectionString);
                var name2 = Utility.GetAccountName(bindingContext.AccountConnectionString);

                string msg = string.Format("Binding has conflicting accounts: {0} and {1}.", name1, name2);
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
                string msg = String.Format("Can't bind parameter '{0}' to type '{1}'. Are you missing a custom model binder or binding attribute ([BlobInput], [BlobOutput], [QueueInput], [QueueOutput], [Table])?", targetParameter.Name, t);
                throw new InvalidOperationException(msg);
            }
            return binder;
        }
    }
}
