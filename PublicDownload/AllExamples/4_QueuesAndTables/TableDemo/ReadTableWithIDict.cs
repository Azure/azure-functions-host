using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleBatch;
using System.IO;

public partial class TableFuncs
{
    // Row from input CSV
    public class Row2
    {
        public Guid guidkey { get; set; }
        public string val1 { get; set; }
        public string val2 { get; set; }
    }
    
    // Row from Azure table conversion
    public class AzureTableRow
    {
        public int intkey { get; set; }
    }

    // Converting each input file will:
    // 1. Convert from guidkey --> intkey via azure lookup table
    // 2. "Normalize" the other fields, data cleaning
    // 3. add a new column, based on the filename (useful if we merge later)
    public static void Convert(
        [BlobInput(@"table-input\data{num}.csv")] IEnumerable<Row2> rows,
        int num,
        [BlobOutput(@"table-output\data{num}.csv")] TextWriter output,
        [Table("convert")] IDictionary<Tuple<string, string>, AzureTableRow> table
        )
    {
        output.WriteLine("intkey, val1, val2, num");
        foreach (var row in rows)
        { 
            var partRowKey = Tuple.Create("const", row.guidkey.ToString());
            int intkey = table[partRowKey].intkey; // azure table lookup

            output.WriteLine("{0}, {1}, {2}, {3}",
                intkey,
                Normalize(row.val1),
                Normalize(row.val2),
                num);
        }
    }

    static string Normalize(string val)
    {
        return val.ToLower();
    }

}
