using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
//using Microsoft.WindowsAzure.StorageClient;
using System.Linq;
using System.Threading;
using SimpleBatch;

namespace TestApp1
{
    class ValueRow
    {
        public int Value { get; set; }
    }

    // Sample class for configuraiton 
    class MyConfig
    {
        public string UserName { get; set; }
        public int Quota { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
        }

        public static void Initialize(IConfiguration config)
        {
            config.Register("TestReg").
                BindBlobInput("input", @"daas-test-input3\{name}.csv").
                BindBlobOutput("output", @"daas-test-input3\{name}.output.csv");
        }

        public static void TestQueue([QueueInput] MyConfig myTestQueue)
        {
            Console.WriteLine("{0},{1}", myTestQueue.UserName, myTestQueue.Quota);
        }

        [NoAutomaticTrigger]
        public static void TestConfig([Config("test.config.txt")] MyConfig options)
        {
            Console.WriteLine("Username:{0}, quoata:{1}", options.UserName, options.Quota);
        }

        [NoAutomaticTrigger]
        public static void TestConfig2(IBinderEx binder)
        {
            MyConfig options = binder.Bind<MyConfig>(new ConfigAttribute("test.config.txt")).Result;

            Console.WriteLine("Username:{0}, quoata:{1}", options.UserName, options.Quota);
        }

        [NoAutomaticTrigger]
        public static void TestCall(ICall call, int value)
        {
            call.QueueCall("TestCall2", new { value = value});
        }

        [NoAutomaticTrigger]
        public static void TestCall2(int value)
        {
            Console.WriteLine("Executed:{0}", value);
        }

        public static void YaoFunc(
            [BlobInput(@"yao-input\{name}.txt")] TextReader input,
            string name, 
            [BlobOutput(@"yao-input\{name}.output")] TextWriter output)
        {
            Console.WriteLine("Hello!");
            Console.WriteLine(name);
            string reader = input.ReadToEnd();
            Console.WriteLine(reader);
            output.WriteLine(reader.Length);
        }
        
        public static void TestReg(TextReader input, TextWriter output)
        {
            string content = input.ReadToEnd();
            output.Write(input);
        }

       
        [NoAutomaticTrigger]
        public static void LongRunning2(
            [BlobInput(@"dtx-table-upload\a.csv")] IEnumerable<ValueRow> foo)
        {
            foreach(var x in foo)
            {
                Console.WriteLine(x.Value);
                Thread.Sleep(1000);
            }
        }


        [Description("Very long running function")]
        public static void LongRunningFunction()
        {
            Console.WriteLine("This is console output.");
            for(int i =0; i <1000; i++)
            {
                Console.WriteLine(i);
                Thread.Sleep(500);
            }
            Console.WriteLine("Success!");
        }

        [Description("Test function with no parameters. ")]
        // [Timer("00:01:00")] // run on a timer
        public static void T()
        {
            Console.WriteLine(DateTime.Now.ToLongTimeString());
            // $$$ Add binding from Blob. 
            // $$$ can write to blob, queue output, etc. 
        }

        [Description("Test function that just produces a silly output")]
        public static void Func2(
            [BlobInput(@"daas-test-input2\{name}.csv")] TextReader values,
            string name,
            [BlobOutput(@"daas-test-input2\{name}.output.txt")] TextWriter output)
        {        
            Console.WriteLine("This is console output. name ={0}", name);
            string val = values.ReadToEnd();
            output.WriteLine("name XYZZZZ={0}", name);
            output.Write(val);
            output.WriteLine("------");
        }
        
        
#if false
        [Description("Test function that just produces a silly output")]
        public static void Func(
            [BlobInput("daas-test-input")] CloudBlob values,
            [BlobOutput("daas-test-output")] CloudBlob output)
        {
            string s = values.DownloadText();
            Console.WriteLine("Output:" + s);

            string result = "**" + s + "**";
            output.UploadText(result);
        }
#endif

        [NoAutomaticTrigger]
        public static void Average(
            [BlobInput("daas-test-input")] IEnumerable<Result> scores,
            [BlobOutput("daas-test-output")] TextWriter result)
        {
            Console.WriteLine("Log message!");
            var x = from item in scores select (double) item.Score;
            result.WriteLine("Average value is: {0}", x.Average());
        }

#if false
        // Basic signature to read in a blob and produce a blob
        // $$$ Error, this produces an infinite loop. 
        // foo.csv --> foo.output.csv --> foo.output.output.csv ...
        public static void Normalize(
            [BlobInput(@"daas-test-input\{name}.csv")] TextReader input,
            [BlobOutput(@"daas-test-input\{name}.output.csv")] TextWriter output)
        {
        }
#endif
                
        public class NormalizedResult
        { 
        }
        private static NormalizedResult Convert(IEnumerable<Result> scores)
        {
            throw new NotImplementedException();
        }
            

        public class Result
        {
            public string Name { get; set; }
            public int Score { get; set; }
        }
    }

    public class TableProgram
    {
        public const string TableName = "testtable1";

        public static void TableWrite([Table(TableName)] IAzureTableWriter writer)
        {
            for (int i = 0; i < 1000; i++)
            {
                Dictionary<string, string> d = new Dictionary<string, string>();
                d["myvalue"] = (i * 10).ToString();
                writer.Write("part", i.ToString(), d);

                Thread.Sleep(100);
            }
        }

        // Test reading from a table!
        public static void TableRead(
            [Table(TableName)] IAzureTableReader reader 
            )
        {
            for (int i = 0; i < 10; i++)
            {
                var d = reader.Lookup("part", i.ToString());                
                var val = d["myvalue"];
                Console.WriteLine(val);
            }
        }

      
    }
}
