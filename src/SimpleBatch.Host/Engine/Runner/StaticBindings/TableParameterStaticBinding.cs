using System.Reflection;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    internal class TableParameterStaticBinding : ParameterStaticBinding
    {
        private string _tableName;

        public string TableName 
        { 
            get
            {
                return _tableName;
            }
            set
            {
                Utility.ValidateAzureTableName(value);
                _tableName = value;
            }
        }

        // $$$ don't think we use this anymore. 
        // True iff we know we have read-only access to the table. 
        // This is used for optimizations. 
        public bool IsReadOnly { get; set; }

        public override void Validate(SimpleBatch.IConfiguration config, ParameterInfo parameter)
        {
            // Table name was already validated in property-setter

            var type = parameter.ParameterType;
            TableParameterRuntimeBinding.GetTableBinderOrThrow(config, type, false);
        }

        public override ParameterRuntimeBinding Bind(IRuntimeBindingInputs inputs)
        {
            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    TableName = this.TableName
                }
            };
        }

        public override ParameterRuntimeBinding BindFromInvokeString(IRuntimeBindingInputs inputs, string invokeString)
        {
            // For convenience, do the right thing with an empty string
            if (string.IsNullOrWhiteSpace(invokeString))
            {
                invokeString = this.TableName;
            }

            return new TableParameterRuntimeBinding
            {
                Table = new CloudTableDescriptor
                {
                    AccountConnectionString = inputs.AccountConnectionString,
                    TableName = invokeString
                }
            };
        }

        public override string Description
        {
            get {
                return string.Format("Access table: {0}", this.TableName);
            }
        }    
    }
}