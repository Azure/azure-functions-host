using System;
using System.Globalization;

namespace Microsoft.WindowsAzure.Jobs
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
                TableClient.ValidateAzureTableName(value);
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
                TableClient.ValidateAzureTableKeyValue(value);
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
                TableClient.ValidateAzureTableKeyValue(value);
                _rowKey = value;
            }
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new TableEntityParameterRuntimeBinding
            {
                Entity = new CloudTableEntityDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    TableName = TableName,
                    PartitionKey = PartitionKey,
                    RowKey = RowKey
                }
            };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            CloudTableEntityDescriptor entityDescriptor = CloudTableEntityDescriptor.Parse(invokeString);
            entityDescriptor.AccountConnectionString = inputs.AccountConnectionString;

            return new TableEntityParameterRuntimeBinding { Entity = entityDescriptor };
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
                if (RowKey == null)
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
