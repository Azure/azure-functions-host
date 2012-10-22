using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using AzureTables;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    public class TableParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudTableDescriptor Table { get; set; }

        public override BindResult Bind(IConfiguration config, IBinder bindingContext, ParameterInfo targetParameter)
        {
            var t = targetParameter.ParameterType;
            return Bind(config, t);
        }

        public BindResult Bind(IConfiguration config, Type type)
        {            
            bool isReadOnly = false; // ### eventually get this from an attribute?

            var binder = config.GetTableBinder(type, isReadOnly);
            if (binder == null)
            {
                string msg = string.Format("Can't bind an azure table to type '{0}'", type.AssemblyQualifiedName);
                throw new InvalidOperationException(msg);
            }

            var ctx = new BindingContext(config, Table.AccountConnectionString);
            var bind = binder.Bind(ctx, type, Table.TableName);
            return bind;
        }

        public override string ConvertToInvokeString()
        {
            return this.Table.TableName;
        }
    }
}