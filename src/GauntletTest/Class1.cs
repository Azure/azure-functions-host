using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.WindowsAzure.Jobs;

namespace GauntletTest
{
    // Provide a single test that's:
    // - easy to excercise on any deployment 
    // - can quickly smoketest features (especially ones that are "fragile" in a production environment)
    // - easy to run and diagnose whether sucess and failure. 
    public class Class1
    {
        // Path where the config file is. 
        const string ConfigBlobName = "GauntletConfig.txt";
        const string ConfigPath = @"sb-gauntlet-functions\" + ConfigBlobName;
        const string TempContainer = @"sb-gauntlet";
        const string TempBlobPath = TempContainer + @"\subdir\test.txt";
        const string TempBlobOutPath = TempContainer + @"\subdir\testOutput.txt";
        const string TableName = "gauntlettable";
        static Tuple<string, string> TableKey = Tuple.Create("1", "first");
        
        const string CookieBlobName = @"cookies\{0}.txt";

        private static void Initialize(IConfiguration config)
        {
            config.Add<Guid>(new GuidBlobBinder());

            string path = string.Format(CookieBlobName, "{cookie}");

            /* TODO: reinstate fluent registration
            config.Register("FromBlob2").
                BindBlobInput("input", TempContainer + @"\" + path).
                BindBlobOutput("receipt", TempContainer + @"\cookies\{cookie}.output");
             * */
        }

        [NoAutomaticTrigger]
        internal static void Start(
            ICall call,
            [Table(TableName)] IDictionary<Tuple<string, string>, object> dict,
            [Config(ConfigBlobName)] Payload val)
        {
            // Pass the guid through. 
            Guid g = Guid.NewGuid();
            Console.WriteLine("Starting a gauntlet run. Cookie: {0}", g);

            // write to table
            dict[TableKey] = new Payload { Name = "Test", Quota = 150, Cookie = g };

            // Config gets modiifed halfway through, so may be in random state unless we rebublish. 
            // Don't care. 
            Console.WriteLine("Initial values: {0},{1}", val.Name, val.Quota);


            IFunctionToken t1 = call.QueueCall("FromCallMiddle", new { value = 1 });
            IFunctionToken t2 = call.QueueCall("FromCallMiddle", new { value = 2 });
            IFunctionToken t3 = call.QueueCall("FromCallMiddle", new { value = 3 });

            call.QueueCall(
                "FromCallJoin", 
                new { name = val.Name, value = val.Quota, cookie = g },
                t1, t2, t3 // prereqs
                );
        }

        [NoAutomaticTrigger]
        public static void FromCallMiddle(int value)
        {
            Thread.Sleep(1000); // help exasperate race conditions. 
        }    

        [NoAutomaticTrigger]
        public static void FromCallJoin(string name, int value, Guid cookie, [QueueOutput] out Payload gauntletQueue)
        {
            gauntletQueue = new Payload
            {
                Name = name,
                Quota = value, 
                Cookie = cookie
            };                            
        }

        public static void FromQueue(
            [QueueInput] Payload gauntletQueue,
            [BlobOutput(ConfigPath)] TextWriter twConfig,
            [Table(TableName)] IDictionary<Tuple<string, string>, Payload> dict, // read as strong object
            [BlobOutput(TempBlobPath)] TextWriter twOther
             )
        {
            var val = dict[TableKey];
            if (val.Name != "Test" || val.Quota != 150)
            {
                throw new Exception("Table read failure");
            }
            if (val.Cookie != gauntletQueue.Cookie)
            {
                throw new Exception("Table cookie has wrong value");
            }

            // Overwrite the config file! Make sure that subsequent runs pick up the update. 
            string msg = @"{ 'Name' : 'Bob', 'Quota' : 2048 }".Replace('\'', '\"');
            twConfig.WriteLine(msg);

            twOther.WriteLine(gauntletQueue.Cookie);
        }

        public static void FromBlob(
            [BlobInput(TempBlobPath)] Guid cookie, // uses custom binder
            [BlobOutput(TempBlobOutPath)] TextWriter receipt, // output blob acts as a "receipt" of execution
            [Config(ConfigBlobName)] Payload val, 
            IBinder binder
            )
        {
            receipt.WriteLine(cookie); // proof that this function executed on latest input.

            // Config should be updated
            if ((val.Name != "Bob") || (val.Quota != 2048))
            {
                throw new Exception("Config wasn't updated");
            }

            string path = string.Format(CookieBlobName, cookie);
            TextWriter tw = binder.BindWriteStream<TextWriter>(TempContainer, path);
            tw.WriteLine(cookie);
        }


        // This function is registered via IConfig
        internal static void FromBlob2(
            Stream input, // bound to BlobInput via config
            TextWriter receipt,
            Guid cookie, // bound as an event arg from the input 
            ICall call)
        {
            receipt.WriteLine(cookie); // proof that this function executed on latest input.

            // Finished the gauntlet, call final function to indicate success.
            call.QueueCall("Done", new { cookie = cookie });   
        }

        // Cookie should match the same one we passed in at start. 
        [NoAutomaticTrigger]
        public static void Done(Guid cookie)
        {
            Console.WriteLine("Success! {0}", cookie);
        }
    }

    public class Payload
    {
        public string Name { get; set; }
        public int Quota { get; set; }
        public Guid Cookie { get; set; }
    }

    // Test a custom Blob binder. 
    public class GuidBlobBinder : ICloudBlobStreamBinder<Guid>
    {
        public Guid ReadFromStream(Stream input)
        {
            string content = new StreamReader(input).ReadToEnd();
            return Guid.Parse(content);
        }

        public void WriteToStream(Guid result, Stream output)
        {
            using (var tw = new StreamWriter(output))
            {
                tw.WriteLine(result.ToString());
            }
        }
    }
}
