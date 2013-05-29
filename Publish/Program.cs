using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using Newtonsoft.Json;

namespace Publish
{

    public class PublishData
    {
        // Copy from here
        public string LocalDir { get; set; } 

        // To here
        public string AccountConnectionString { get; set; }
        public string Container { get; set; }

        // And then ping this service to refresh
        public string ServiceUrl { get; set; }

        public CloudStorageAccount GetAccount()
        {
            return CloudStorageAccount.Parse(this.AccountConnectionString);
        }
        public string GetAccountName()
        {
            return GetAccount().Credentials.AccountName;
        }
    }

    // Args we send to the web request
    public class RegisterArgs
    {
        public string AccountConnectionString { get; set; }
        public string ContainerName { get; set; }
    }

    // response we get back from the web request.
    public class RegisterArgsResponse
    {
        public string ResultUri { get; set; }
    }

    // Utility for publishing a project 
    // 1. Xcopy to blob storage
    // 2. ping server
    class Program
    {
        [STAThread] // We'll pop a UI
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "dbg")
            {
                args = args.Skip(1).ToArray();
                Debugger.Launch();
            }

            if (args.Length < 1)
            {
                Console.WriteLine(
@"Usage:
Publish <filename>

Filename is a full path to a config file providing publish information.
If filename does not exist, you're prompted to create the config file.

This will:
  1) xcopy the dlls to a container
  2) register the functions with the service
  3) wait for registration to succeed and print results
");
                return;
            }
            string configName = args[0];
            if (!File.Exists(configName))
            {
                Console.WriteLine("No config file exists at: {0}", configName);
                //File.WriteAllText(configName, JsonConvert.SerializeObject(EnterData()));

                InputDialog dlg = new InputDialog();
                dlg.ShowDialog();
                if (dlg.Data == null)
                {
                    Console.WriteLine("Aborted.");
                    return;
                }

                File.WriteAllText(configName, JsonConvert.SerializeObject(dlg.Data, Formatting.Indented));                

            }

            PublishData data = JsonConvert.DeserializeObject<PublishData>(File.ReadAllText(configName));

            Console.WriteLine(
@"Uploading functions 
from: {0} 
  to: {1}\{2}", data.LocalDir, data.GetAccountName(), data.Container);

            UploadFunctions(data);

            Console.WriteLine(@"Pinging service: {0}", data.ServiceUrl);
            PingAndWait(data);
        }

        private static void UploadFunctions(PublishData data)
        {
            CloudStorageAccount a = data.GetAccount();
            CloudBlobClient client = a.CreateCloudBlobClient();
            CloudBlobContainer c = client.GetContainerReference(data.Container);

            // empty container first?
            c.CreateIfNotExist();

            foreach (var file in Directory.EnumerateFiles(data.LocalDir))
            {
                string ext = Path.GetExtension(file).ToLower();
                if (ext == ".pdb")
                {
                    // include PDBs so that we get source information for exceptions
                    // continue; // skip pdbs.
                }

                string name = Path.GetFileName(file);
                CloudBlob blob = c.GetBlobReference(name);

                Console.WriteLine("  ({1:0.0}kb) {0}", Path.GetFileName(file), new FileInfo(file).Length / 1000.0);
                blob.UploadFile(file);                
            }

            // Drop reminder
            {
                CloudBlob blob = c.GetBlobReference("source.txt");
                blob.UploadText(string.Format(
@"These functions were automatically uploaded from: {0}
On machine: {1}",
                data.LocalDir, Environment.MachineName));
            }
        }


        static TResult PostJson<TRequest, TResult>(string url, TRequest input)
        {
            var json = JsonConvert.SerializeObject(input);

            byte[] bytes = Encoding.UTF8.GetBytes(json);

            WebRequest request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length; // set before writing to stream
            var stream = request.GetRequestStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Close();

            var response = request.GetResponse(); // does the actual web request

            var stream2 = response.GetResponseStream();
            var text = new StreamReader(stream2).ReadToEnd();
            stream2.Close();

            TResult val = JsonConvert.DeserializeObject<TResult>(text);

            return val;
        }

        static void PingAndWait(PublishData data)
        {
            RegisterArgs args = new RegisterArgs { AccountConnectionString = data.AccountConnectionString, ContainerName = data.Container };


            string uri = string.Format(@"{0}/Api/Execution/RegisterFunction", data.ServiceUrl);

            RegisterArgsResponse val = PostJson<RegisterArgs, RegisterArgsResponse>(uri, args);
            
            // Now loop waiting for uri.
            string lastResult = null;
            while (true)
            {
                var result = TryDownloadBlob(val.ResultUri);
                if (result != null)
                {
                    if (lastResult != result)
                    {
                        Console.WriteLine();
                        Console.WriteLine(result);
                        lastResult = result;
                    }

                    if (result.Contains("DONE:"))
                    {
                        break;
                    }
                }
                Thread.Sleep(1000);
                Console.Write(".");
            }
        }

        private static WebClient _client = new WebClient();
        public static string TryDownloadBlob(string uri)
        {
            try
            {
                return _client.DownloadString(uri);
            }
            catch
            {
                return null;
            }
        }


        public static PublishData EnterData()
        {
            PublishData data = new PublishData();

            Console.WriteLine("Enter account name:");
            string accountName = Console.ReadLine();

            Console.WriteLine("Enter account secret key:");
            string accountKey = Console.ReadLine();

            var account = new CloudStorageAccount(new StorageCredentialsAccountAndKey(accountName, accountKey), false);
            data.AccountConnectionString = account.ToString(exportSecrets: true);

            Console.WriteLine("Enter destination container name to upload to:");
            data.Container = Console.ReadLine();

            Console.WriteLine("Enter local directory to copy from:");
            data.LocalDir = Console.ReadLine();

            Console.WriteLine("Enter service URL to ping:");
            data.ServiceUrl = Console.ReadLine();


            return data;
        }
    }

}
