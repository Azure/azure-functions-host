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

        /// <summary>
        /// Initializes a new instance of the TableAttribute class.
        /// </summary>
        /// <param name="tableName">The name of the table containing the entity.</param>
        /// <param name="partitionKey">The partition key of the entity.</param>
        /// <param name="rowKey">The row key of the entity.</param>
        public TableAttribute(string tableName, string partitionKey, string rowKey)
        {
            if (tableName == null)
            {
                throw new ArgumentNullException("tableName");
            }

            if (partitionKey == null)
            {
                throw new ArgumentNullException("partitionKey");
            }

            if (rowKey == null)
            {
                throw new ArgumentNullException("rowKey");
            }

            TableName = tableName;
            PartitionKey = partitionKey;
            RowKey = rowKey;
        }

        // Beware of table name restrictions.
        /// <summary>
        /// Gets the name of the table to bind to. If empty, the name of the method parameter is used
        /// as the table name.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// When binding to a table entity, specifies the partition key of the entity.
        /// </summary>
        /// <remarks>
        /// Route parameters (like {name}) are currently not supported.
        /// </remarks>
        public string PartitionKey { get; private set; }

        /// <summary>
        /// When binding to a table entity, specifies the row key of the entity.
        /// </summary>
        /// <remarks>
        /// Route parameters (like {name}) are currently not supported.
        /// </remarks>
        public string RowKey { get; private set; }

        /// <inheritdoc />
        public override string ToString()
        {
            if (RowKey == null)
            {
                return String.Format(CultureInfo.InvariantCulture, "[Table({0})]", TableName);
            }
            else
            {
                return String.Format(CultureInfo.InvariantCulture,
                    "[Table({0}, {1}, {2})]", TableName, PartitionKey, RowKey);
            }
        }
    }
}
