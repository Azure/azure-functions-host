using System;
using System.Globalization;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.BinderProviders;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs
{
    internal class TableEntityParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudTableEntityDescriptor Entity { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            Type targetType = targetParameter.ParameterType;
            TableClient.VerifyDefaultConstructor(targetType);

            if (TableClient.ImplementsITableEntity(targetType))
            {
                return BindITableEntityGeneric(targetType, config, bindingContext);
            }
            else
            {
                return BindPocoGeneric(targetType, config, bindingContext);
            }
        }

        private BindResult BindITableEntityGeneric(Type type, IConfiguration config, IBinderEx bindingContext)
        {
            // Call BindITableEntity<T>(config, bindingContext);
            MethodInfo genericMethod = typeof(TableEntityParameterRuntimeBinding).GetMethod("BindITableEntity", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo methodInfo = genericMethod.MakeGenericMethod(type);
            Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult> invoker =
                (Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult>)
                Delegate.CreateDelegate(
                typeof(Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult>), methodInfo);
            return invoker.Invoke(this, config, bindingContext);
        }

        private BindResult BindPocoGeneric(Type type, IConfiguration config, IBinderEx bindingContext)
        {
            // Call BindPoco<T>(config, bindingContext);
            MethodInfo genericMethod = typeof(TableEntityParameterRuntimeBinding).GetMethod("BindPoco", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo methodInfo = genericMethod.MakeGenericMethod(type);
            Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult> invoker =
                (Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult>)
                Delegate.CreateDelegate(
                typeof(Func<TableEntityParameterRuntimeBinding, IConfiguration, IBinderEx, BindResult>), methodInfo);
            return invoker.Invoke(this, config, bindingContext);
        }

        private BindResult BindITableEntity<T>(IConfiguration config, IBinderEx bindingContext) where T : ITableEntity, new()
        {
            return new TableEntityBinder<T>().Bind(bindingContext, Entity.TableName, Entity.PartitionKey, Entity.RowKey);
        }

        private BindResult BindPoco<T>(IConfiguration config, IBinderEx bindingContext) where T : new()
        {
            return new PocoTableEntityBinder<T>().Bind(bindingContext, Entity.TableName, Entity.PartitionKey, Entity.RowKey);
        }

        public override string ConvertToInvokeString()
        {
            return String.Format(CultureInfo.InvariantCulture,
                "{0}/{1}/{2}", Entity.TableName, Entity.PartitionKey, Entity.RowKey);
        }
    }
}
