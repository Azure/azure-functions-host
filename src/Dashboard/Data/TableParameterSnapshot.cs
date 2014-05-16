using Microsoft.Azure.Jobs.Host.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Table")]
    public class TableParameterSnapshot : ParameterSnapshot
    {
        public string TableName { get; set; }

        public override string Description
        {
            get
            {
                return string.Format("Access table: {0}", this.TableName);
            }
        }

        public override string Prompt
        {
            get
            {
                return "Enter the table name";
            }
        }

        public override string DefaultValue
        {
            get
            {
                return TableName;
            }
        }
    }
}
