using Microsoft.WindowsAzure.Storage.Blob;
using SimpleBatch; // Via Nuget package
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using System.Web.Helpers;
using DataAccess;

namespace TestLocalHost
{
    class Custom
    {
        public string _value;
    }
#if true
    class MyBinder : ICloudBlobStreamBinder<Custom>
    {
        public Custom ReadFromStream(Stream input)
        {
            using (TextReader tr = new StreamReader(input))
            {
                return new Custom { _value = tr.ReadToEnd() };
            }
        }

        public void WriteToStream(Custom result, Stream output)
        {
            using (TextWriter tw = new StreamWriter(output))
            {
                tw.WriteLine(result._value);
            }
        }
    }
#endif

    public class TableFuncs
    {
        public class OrgEntry
        {
            public string Email { get; set; }
            public string ReportsToEmail { get; set; }
            public string StandardTitle { get; set; }
        }


        // Ingress from the blob to an Azure Table with (PartitionKey, RowKey, IntKey)
        [NoAutomaticTrigger]
        public static void Ingress(
            [BlobInput(@"tableupload\{name}")] TextReader input,
            [Table("convert")] IDictionary<Tuple<string, string>, OrgEntry> table
            )
        {
            // grab a CSV reader from Nuget. 
            var rows = DataAccess.DataTable.New.Read(input).RowsAs<OrgEntry>();

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Email))
                {
                    continue;
                }
                var partRowKey = Tuple.Create("const", row.Email);
                table[partRowKey] = row; // azure table write
            }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var t = typeof(TestBinder.WebImageBinder);
            //string ac1 = @"DefaultEndpointsProtocol=https;AccountName=??;AccountKey=???";

            var host = new Host();

#if true
            // (1)
            // Invoke a function directly (such as with a unit test)
            //TestFuncWriter(Console.Out, "test");


            {
                var m = typeof(TableFuncs).GetMethod("Ingress");
                host.Call(m, new { name = "satya-org.csv"  });
            }
#endif
            // (2)
            // Invoke a function directly via SimpleBatch model binding. 

            //var m = typeof(Program).GetMethod("TestFuncWriter2");
            //var m = typeof(Program).GetMethod("WriteTimedBlob");
            //var m = typeof(Program).GetMethod("StartY");
            //host.Call(m, new { name = "xyz", value = "dog" });            

            // (3)
            // Start a local copy of the trigger service. 
            //host.RunAndBlock();
        }

        public static void Resize(
    [BlobInput(@"images\input\{name}")] WebImage input,
    [BlobOutput(@"images\output\{name}")] out WebImage output)
        {
            var width = 80;
            var height = 80;
                        
            output = input.Resize(width, height);
            output.AddTextWatermark("Azure!", fontSize: 20, fontColor : "red");
        }

        [NoAutomaticTrigger]
        [Description("This is a test function")]
        public static void TestHelper()
        {            
            Console.WriteLine("test");
        }

        [NoAutomaticTrigger]
        public static void WordCount2(Guid g)
        {
        }

        [NoAutomaticTrigger]
        public static void WordCount(
            [BlobInput(@"test6\{name}.txt")] TextReader reader
            )
        {
            var content = reader.ReadToEnd();
            Console.WriteLine(content);
        }

        public static void ReadCustom(
            [BlobInput(@"test6\{name}.txt")] Custom reader,
            [BlobOutput(@"test6\{name}.outx")] out Custom writer
        )
        {
            writer = new Custom 
            { 
                _value = reader._value 
            };
        }

        public static void TestFuncWriter2(
            [BlobOutput(@"test6\{name}.txt")] TextWriter writer,
            string value)
        {
            writer.WriteLine(value);
        }

        public static void WriteDirect(
            [BlobOutput(@"test6\{name}.1txt")] ICloudBlob writer,
            string value)
        {
            byte[] content = Encoding.UTF8.GetBytes(value);
            var stream  = new MemoryStream(content);
            writer.UploadFromStream(stream);
        }

        [NoAutomaticTrigger]
        public static void WriteTimedBlob(IBinder binder)
        {
            using (var writer = binder.BindWriteStream<TextWriter>("test6", "Blob6.txt"))
            {
                writer.Write(DateTime.Now);
            }
        }
#if false
        

        public static void TestFunc97(
            [BlobInput(@"test6\{name}.txt")] TextReader reader,
            string name,
            [BlobOutput(@"test6\{name}.out")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }

        public static void Chain1(
    [BlobInput(@"test6\{name}.out")] TextReader reader,
    string name,
    [BlobOutput(@"test6\{name}.out1")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }

        public static void Chain2(
[BlobInput(@"test6\{name}.out1")] TextReader reader,
string name,
[BlobOutput(@"test6\{name}.out2")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }

        public static void Chain2b(
[BlobInput(@"test6\{name}.out1")] TextReader reader,
string name,
[BlobOutput(@"test6\{name}.done1")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }

        public static void Chain3(
[BlobInput(@"test6\{name}.out2")] TextReader reader,
string name,
[BlobOutput(@"test6\{name}.out3")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }

        public static void Chain4(
[BlobInput(@"test6\{name}.out3")] TextReader reader,
string name,
[BlobOutput(@"test6\{name}.done2")] TextWriter writer)
        {
            writer.WriteLine(name);
            writer.WriteLine(reader.ReadToEnd());
        }
#endif
    }
}


