using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SimpleBatch;
using System.IO;

// Convert from guidkey --> intkey via azure lookup table
public interface ILookup
{
    int Lookup(Guid guid);
}



public partial class TableFuncs2
{
    // Row from input CSV
    public class Row2
    {
        public Guid guidkey { get; set; }
        public string val1 { get; set; }
        public string val2 { get; set; }
    }

    // Converting each input file will:
    // 1. Convert from guidkey --> intkey via azure lookup table
    // 2. "Normalize" the other fields, data cleaning
    // 3. add a new column, based on the filename (useful if we merge later)
    public static void Convert(
        [BlobInput(@"input\data{num}.csv")] IEnumerable<Row2> rows,
        int num,
        [BlobOutput(@"output\data{num}.csv")] TextWriter output,
        [Table("convert")] ILookup table
        )
    {
        output.WriteLine("intkey, val1, val2, num");
        foreach (var row in rows)
        {
            int intkey = table.Lookup(row.guidkey); // azure table lookup

            output.Write("{0}, {1}, {2}, {3}",
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
