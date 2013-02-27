using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleBatch;

public partial class TableFuncs
{
    // This class matches the CSV schema we're loading. Each property is a column name.
    public class Row
    {
        public Guid guidkey { get; set; }
        public int intkey { get;set; }
    }

    #region Simple Ingress
    // Ingress from the blob to an Azure Table with (PartitionKey, RowKey, IntKey)
    [NoAutomaticTrigger]
    public static void Ingress(
        [BlobInput(@"table-uploads\key.csv")] IEnumerable<Row> rows, 
        [Table("convert")] IDictionary<Tuple<string,string>, object> table
        )    
    {
        foreach(var row in rows)
        {
            var partRowKey = Tuple.Create("const", row.guidkey.ToString());
            table[partRowKey] = new { intkey = row.intkey }; // azure table write
        }
    }
    #endregion

    #region Distributed Ingress using Queues

    // Helper for partitioning a table.
    public class IngressPayload
    {
        public int StartRow { get; set; }
        public int Count { get; set; }
    }

    // Ingress from the blob to an Azure Table with (PartitionKey, RowKey, IntKey)
    [NoAutomaticTrigger]
    public static void Ingress2(
        [BlobInput(@"table-uploads\key.csv")] IEnumerable<Row> rows,
        [QueueOutput] IQueueOutput<IngressPayload> queueingress2
        )
    {
        int count = rows.Count();
        int N = 10 * 1000;
        for (int i = 0; i < count; i += N)
        {
            queueingress2.Add(new IngressPayload { StartRow = i, Count = N });
        }
    }

    public static void Ingress2Worker(
        [QueueInput] IngressPayload queueingress2, // invoked on new queue message
        [BlobInput(@"table-uploads\key.csv")] IEnumerable<Row> rows,
        [Table("convert")] IDictionary<Tuple<string, string>, object> table
        )
    {
        foreach (var row in rows.Skip(queueingress2.StartRow).Take(queueingress2.Count))
        {
            var partRowKey = Tuple.Create("const", row.guidkey.ToString());
            table[partRowKey] = new { intkey = row.intkey }; // azure table write
        }
    }
    #endregion

    #region Distributed Ingress using ICall 
    // Ingress from the blob to an Azure Table with (PartitionKey, RowKey, IntKey)
    [NoAutomaticTrigger]
    public static void Ingress3(
        [BlobInput(@"table-uploads\key.csv")] IEnumerable<Row> rows,
        ICall call
        )
    {
        int count = rows.Count();
        int N = 10 * 1000;
        for (int i = 0; i < count; i += N)
        {
            call.QueueCall("Ingress3Worker", new { StartRow = i, Count = N });
        }
    }

    [NoAutomaticTrigger] // invoked from Ingress3 using ICall
    public static void Ingress3Worker(
        int StartRow,
        int Count,
        [BlobInput(@"table-uploads\key.csv")] IEnumerable<Row> rows,
        [Table("convert")] IDictionary<Tuple<string, string>, object> table
        )
    {
        foreach (var row in rows.Skip(StartRow).Take(Count))
        {
            var partRowKey = Tuple.Create("const", row.guidkey.ToString());
            table[partRowKey] = new { intkey = row.intkey }; // azure table write
        }
    }
    #endregion

}

