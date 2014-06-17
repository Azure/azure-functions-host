using System;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityPath
    {
        public string TableName { get; set; }

        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public static TableEntityPath Parse(string value)
        {
            TableEntityPath path;

            if (!TryParse(value, out path))
            {
                throw new InvalidOperationException("Table entity identifiers must be in the format TableName/PartitionKey/RowKey.");
            }

            return path;
        }

        public static bool TryParse(string value, out TableEntityPath path)
        {
            if (value == null)
            {
                path = null;
                return false;
            }

            string[] components = value.Split(new char[] { '/' });
            if (components.Length != 3)
            {
                path = null;
                return false;
            }

            path = new TableEntityPath
            {
                TableName = components[0],
                PartitionKey = components[1],
                RowKey = components[2]
            };
            return true;
        }
    }
}
