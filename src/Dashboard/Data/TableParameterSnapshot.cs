using System;
using System.Globalization;
using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Table")]
    public class TableParameterSnapshot : ParameterSnapshot
    {
        public string TableName { get; set; }

        public override string Description
        {
            get { return String.Format(CultureInfo.CurrentCulture, "Access table: {0}", TableName); }
        }

        public override string AttributeText
        {
            get { return String.Format(CultureInfo.CurrentCulture, "[Table(\"{0}\")]", TableName); }
        }

        public override string Prompt
        {
            get { return "Enter the table name"; }
        }

        public override string DefaultValue
        {
            get { return TableName; }
        }
    }
}
