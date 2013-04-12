using System.Collections.Generic;

namespace AzureTables
{
    // Abstracts away access to AzureTable vs. In-memory storage. 
    // Used by AzureTable<TValue> class. That class has as much policy as possible, such as:
    // - serialization, invoking JSON 
    // - strong-binding and .NET type coercion (eg, enums, parsing)
    // - ISelfWatch and timing implementations
    // - duplicate part/row key handling 
    // - error checking, etc. 
    abstract class TableCore
    {
        // Delete the table and block until the table is fully deleted and can be recreated. 
        // This can take minutes in a live azure case.
        public abstract void DeleteTable();

        // Async delete the table and return immediately. 
        // Table is in an unknown state after this returns. 
        public abstract void DeleteTableAsync();

        // Delete just the partition. Non-null.
        public abstract void DeleteTablePartition(string partitionKey);

        // Delete a specific partition and row-key.
        // Nop if missing. 
        public abstract void DeleteTableRow(string partitionKey, string rowKey);

        // Return null on exception ($$$ empty?)
        // if partitionKey == null, then read the entire table.
        // Else return the partition, sorted by row-key.
        public abstract IEnumerable<GenericEntity> Enumerate(string partitionKey);

        // Return null if not found. 
        public abstract GenericEntity Lookup(string partitionKey, string rowKey);

        public abstract ITableCorePartitionWriter NewPartitionWriter(string partitionKey);
    }

    // Writes objects to a specific partition. 
    interface ITableCorePartitionWriter
    {
        // Expected that entity.PartitionKey is set to the right partition.
        // !!! Policy on duplicates?
        void AddObject(GenericEntity entity);

        void Flush();
    }
}