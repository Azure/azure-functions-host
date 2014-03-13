using System;
using System.Reflection;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class TableParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudTableDescriptor Table { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            return Bind(config, t, bindingContext.CancellationToken, bindingContext.FunctionInstanceGuid);
        }

        public BindResult Bind(IConfiguration config, Type type, CancellationToken cancellationToken, FunctionInstanceGuid instance)
        {            
            bool isReadOnly = false; // ### eventually get this from an attribute?

            ICloudTableBinder binder = GetTableBinderOrThrow(config, type, isReadOnly);

            IRuntimeBindingInputs inputs = new RuntimeBindingInputs(Table.AccountConnectionString);
            IBinderEx ctx = new BindingContext(config, inputs, instance, notificationService : null,
                cancellationToken: cancellationToken);
            var bind = binder.Bind(ctx, type, Table.TableName);
            return bind;
        }

        public static ICloudTableBinder GetTableBinderOrThrow(IConfiguration config, Type type, bool isReadOnly)
        {
            ICloudTableBinder binder = config.GetTableBinder(type, isReadOnly);
            if (binder == null)
            {
                string msg = string.Format("Can't bind an Azure table to type '{0}'", type.AssemblyQualifiedName);
                throw new InvalidOperationException(msg);
            }
            return binder;
        }

        public override string ConvertToInvokeString()
        {
            return this.Table.TableName;
        }
    }
}
