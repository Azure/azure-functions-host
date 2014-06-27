using System;
using System.IO;
using Xunit;

namespace Microsoft.Azure.Jobs.Host.IntegrationTests
{
    /// <summary>
    /// Summary description for ModelbindingTests
    /// </summary>
    public class ModelBindingTests
    {
        [Fact]
        public void TestBindToIBinder()
        {
            var account = TestStorage.GetAccount();

            TestBlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            lc.Call("TestBinder");

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "directout.txt");
            Assert.Equal("output", content);
        }

        [Fact]
        public void TestModelBinding()
        {
            var account = TestStorage.GetAccount();

            TestBlobClient.DeleteContainer(account, "daas-test-input");
            TestBlobClient.WriteBlob(account, "daas-test-input", "input.txt", "abc");

            var lc = TestStorage.New<Program>(account, new Type[] { typeof(ModelCloudBlobStreamBinder) });
            lc.CallOnBlob("Func", @"daas-test-input/input.txt");

            string content = TestBlobClient.ReadBlob(account, "daas-test-input", "output.txt");
            Assert.Equal("*abc*", content);
        }

        class ModelCloudBlobStreamBinder : ICloudBlobStreamBinder<Model>
        {
            public Model ReadFromStream(Stream input)
            {
                TextReader reader = new StreamReader(input);
                string text = reader.ReadToEnd();
                return new Model { Value = text };
            }

            public void WriteToStream(Model value, Stream output)
            {
                TextWriter writer = new StreamWriter(output);
                writer.Write(value.Value);
                writer.Flush();
            }
        }

        class Program
        {
            [NoAutomaticTrigger]
            public static void TestBinder(IBinder binder)
            {
                TextWriter tw = binder.Bind<TextWriter>(new BlobAttribute("daas-test-input/directout.txt"));
                tw.Write("output");

                // closed automatically 
            }


            public static void Func(
                [BlobTrigger(@"daas-test-input/input.txt")] Model input,
                [Blob(@"daas-test-input/output.txt")] out Model output)
            {
                output = new Model { Value = "*" + input.Value + "*" };
            }
        }

        class Model
        {
            public string Value;
        }
    }
}
