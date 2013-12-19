namespace AzureTables
{
    // Writes objects to a specific partition. 
    interface ITableCorePartitionWriter
    {
        // Expected that entity.PartitionKey is set to the right partition.
        // Entity is unique within a batch (eg, between calls to Flush). 
        // If entity already exists from a previous batch, then overwrite (eg, this is upsert). 
        void AddObject(GenericEntity entity);

        void Flush();
    }
}
