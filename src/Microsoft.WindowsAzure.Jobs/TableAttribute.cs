using System;
using System.Globalization;

namespace Microsoft.WindowsAzure.Jobs
{
    /// <summary>
    /// Represents an attribute that is used to provide details about how a Windows Azure Table is
    /// bound as a method parameter for input and output.
    /// The method parameter type by default can be either an IDictionary&lt;Tuple&lt;string,string&gt;, object&gt; or
    /// an IDictionary&lt;Tuple&lt;string,string&gt;, UserDefinedType&gt;.
    /// The two properties of the Tuple key represent the partition key and row key, respectively.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class TableAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the TableAttribute class.
        /// </summary>
        public TableAttribute()
        {
        }

        /// <summary>
        /// Initializes a new instance of the TableAttribute class.
        /// </summary>
        /// <param name="tableName">If empty, the name of the method parameter is used
        /// as the table name.</param>
        public TableAttribute(string tableName)
        {
            TableName = tableName;
        }

        // Beware of table name restrictions.
        /// <summary>
        /// Gets the name of the table to bind to. If empty, the name of the method parameter is used
        /// as the table name.
        /// </summary>
        public string TableName { get; private set; }

        /// <inheritdoc />
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "[Table({0})]", TableName);
        }
    }
}
