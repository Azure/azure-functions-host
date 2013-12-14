using System;
using System.Data.Services.Client;
using System.Data.Services.Common;
using System.Linq;
using System.Xml.Linq;
using AzureTables;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Proviide an atomic adjsut on tables.     
    internal static partial class Utility
    {
        public static int AtomicAdjust(CloudStorageAccount account, string tableName,
            string partitionKey, string rowKey, string propName,
            int delta)
        {
            CloudTableClient tableClient = account.CreateCloudTableClient();
            return AtomicAdjust(tableClient, tableName, partitionKey, rowKey, propName, delta);
        }        

        // Atomically adjust the given integeger property name in the given table.
        // Returns new value
        // Loop on contention failure. 
        public static int AtomicAdjust(CloudTableClient tableClient, string tableName,
            string partitionKey, string rowKey, string propName,
            int delta)
        {

            int retryCount = 50;
            while (retryCount > 0)
            {                
                tableClient.CreateTableIfNotExist(tableName);
                TableServiceContext ctx = tableClient.GetDataServiceContext();


                SingleEntityReader closure = new SingleEntityReader { PropName = propName };
                ctx.IgnoreMissingProperties = true;
                ctx.ReadingEntity += closure.ctx_ReadingEntity;
                ctx.WritingEntity += closure.ctx_WritingEntity;

                // Read single property, as an integer
                var x = from o in ctx.CreateQuery<SingleEntity>(tableName)
                        where o.PartitionKey == partitionKey && o.RowKey == rowKey
                        select o;
                SingleEntity all = x.First();

                if (delta == 0)
                {
                    return all.Value;
                }

                if (all == null)
                {
                    throw new InvalidOperationException("Counter row does not exist");
                }
                all.Value += delta;

                // Update back
                // Important that this is the same context object that we read from.
                // That will tracks ETags and fail if another writer came in.
                ctx.UpdateObject(all);

                try
                {
                    // Merge by default. Important since we're only updating one property.
                    ctx.SaveChanges();
                }
                catch (DataServiceRequestException)
                {
                    retryCount--;
                    // Likely a concurrency hit. Retry
                    continue;
                }

                // Update successful, we're done. 
                //Console.WriteLine("{0} --> {1}", delta, all.Value);
                return all.Value;
            }

            // $$$ Should we just loop infinitely?
            throw new InvalidOperationException("Failed to update counter.");
        }

        class SingleEntityReader
        {
            public string PropName;

            public void ctx_WritingEntity(object sender, ReadingWritingEntityEventArgs args)
            {
                SingleEntity entity = args.Entity as SingleEntity;
                if (entity == null)
                {
                    return;
                }

                XElement properties = args.Data.Descendants(AzureTableConstants.MetadataNamespace + "properties").First();

                {
                    // framework will handle row + partition keys. 
                    string columnName = this.PropName;
                    string edmTypeName = "Edm.Int32";
                    string value = entity.Value.ToString();
                    XElement e = new XElement(AzureTableConstants.DataNamespace + columnName, value);
                    e.Add(new XAttribute(AzureTableConstants.MetadataNamespace + "type", edmTypeName));

                    properties.Add(e);
                }
            }

            public void ctx_ReadingEntity(object sender, ReadingWritingEntityEventArgs args)
            {
                SingleEntity entity = args.Entity as SingleEntity;
                if (entity == null)
                {
                    return;
                }

                // read each property, type and value in the payload   
                var properties = args.Entity.GetType().GetProperties();
                var q = from p in args.Data.Element(AzureTableConstants.AtomNamespace + "content")
                                        .Element(AzureTableConstants.MetadataNamespace + "properties")
                                        .Elements()
                        where properties.All(pp => pp.Name != p.Name.LocalName)
                        where p.Name.LocalName == this.PropName
                        select p.Value;

                var q2 = q.FirstOrDefault();
                if (q2 != null)
                {
                    int value;
                    int.TryParse(q2, out value);
                    entity.Value = value;
                }
            }
        }

        [DataServiceKey("PartitionKey", "RowKey")]
        internal class SingleEntity
        {
            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
            internal int Value; // not a property, so won't get written to xml by default handlers.
        }
    }
}