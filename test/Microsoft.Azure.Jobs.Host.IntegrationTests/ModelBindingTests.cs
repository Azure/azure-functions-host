using System;
using System.IO;
using Microsoft.Azure.Jobs;
using Microsoft.WindowsAzure.Storage.Blob;
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

            var lc = TestStorage.New<Program>(account);
            IConfiguration config = lc.Configuration;
            config.BlobBinders.Add(new ModelBlobBinderProvider());
            config.CloudBlobStreamBinderTypes.Add(typeof(ModelCloudBlobStreamBinder));
            config.BlobBinders.Add(new SimpleBinderProvider<Model>(new ModelCloudBlobStreamBinder()));
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
                throw new NotSupportedException();
            }
        }

        class ModelBlobBinderProvider : ICloudBlobBinderProvider
        {
            // Helper to include a cleanup function with bind result
            class BindCleanupResult : BindResult
            {
                public Action<object> Cleanup;

                public override void OnPostAction()
                {
                    if (Cleanup != null)
                    {
                        Cleanup(this.Result);
                    }
                }
            }

            class ModelOutputBlobBinder : ICloudBlobBinder
            {
                public BindResult Bind(IBinderEx bindingContext, string containerName, string blobName, Type targetType)
                {
                    CloudBlockBlob blob = GetBlob(bindingContext.StorageConnectionString, containerName, blobName);

                    // On input
                    return new BindCleanupResult
                    {
                        Result = null,
                        Cleanup = (newResult) =>
                        {
                            Model model = (Model)newResult;
                            blob.UploadText(model.Value);
                        }
                    };
                }
            }

            public ICloudBlobBinder TryGetBinder(Type targetType)
            {
                if (targetType == typeof(Model))
                {
                    return new ModelOutputBlobBinder();
                }
                return null;
            }

            private static CloudBlockBlob GetBlob(string accountConnectionString, string containerName, string blobName)
            {
                var account = Utility.GetAccount(accountConnectionString);
                var client = account.CreateCloudBlobClient();
                var c = client.GetContainerReference(containerName);
                var blob = c.GetBlockBlobReference(blobName);
                return blob;
            }
        }

        class Program
        {
            [Description("Invoke with an IBinder")]
            public static void TestBinder(IBinder binder)
            {
                TextWriter tw = binder.BindWriteStream<TextWriter>("daas-test-input", "directout.txt");
                tw.Write("output");

                // closed automatically 
            }


            public static void Func(
                [BlobTrigger(@"daas-test-input/input.txt")] Model input,
                [BlobOutput(@"daas-test-input/output.txt")] out Model output)
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
