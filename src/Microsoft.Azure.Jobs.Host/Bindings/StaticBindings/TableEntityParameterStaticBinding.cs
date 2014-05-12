using System;
using System.Globalization;

namespace Microsoft.Azure.Jobs
{
    internal class TableEntityParameterStaticBinding : ParameterStaticBinding
    {
        private string _tableName;
        private string _partitionKey;
        private string _rowKey;

        public string TableName
        {
            get
            {
                return _tableName;
            }
            set
            {
                if (!RouteParser.HasParameterNames(value))
                {
                    TableClient.ValidateAzureTableName(value);
                }

                _tableName = value;
            }
        }

        public string PartitionKey
        {
            get
            {
                return _partitionKey;
            }
            set
            {
                if (!RouteParser.HasParameterNames(value))
                {
                    TableClient.ValidateAzureTableKeyValue(value);
                }

                _partitionKey = value;
            }
        }

        public string RowKey
        {
            get
            {
                return _rowKey;
            }
            set
            {
                if (!RouteParser.HasParameterNames(value))
                {
                    TableClient.ValidateAzureTableKeyValue(value);
                }

                _rowKey = value;
            }
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            CloudTableEntityDescriptor entity = CloudTableEntityDescriptor.ApplyNames(TableName, PartitionKey, RowKey, inputs.NameParameters);
            return BindCore(Name, entity, inputs);
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            CloudTableEntityDescriptor entity = CloudTableEntityDescriptor.Parse(invokeString);
            return BindCore(Name, entity, inputs);
        }

        private static ParameterRuntimeBinding BindCore(string name, CloudTableEntityDescriptor entity, IRuntimeBindingInputs inputs)
        {
            entity.Validate();
            entity.AccountConnectionString = inputs.AccountConnectionString;

            return new TableEntityParameterRuntimeBinding { Name = name, Entity = entity };
        }

        public override string Description
        {
            get
            {
                return string.Format(CultureInfo.CurrentCulture,
                    "Access table entity: {0} (PK: {1}, RK: {2})", TableName, PartitionKey, RowKey);
            }
        }

        public override string Prompt
        {
            get
            {
                return "Enter the table entity identifier (TableName/PartitionKey/RowKey)";
            }
        }

        public override string DefaultValue
        {
            get
            {
                if (RouteParser.HasParameterNames(TableName) ||
                    RouteParser.HasParameterNames(PartitionKey) ||
                    RouteParser.HasParameterNames(RowKey))
                {
                    return null;
                }
                else
                {
                    return String.Format(CultureInfo.CurrentCulture, "{0}/{1}/{2}", TableName, PartitionKey, RowKey);
                }
            }
        }
    }
}
