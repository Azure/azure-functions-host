using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.JobsUnitTests
{
    /// <summary>
    /// Summary description for ModelbindingTests
    /// </summary>
    [TestClass]
    public class ModelBindingTests
    {
        [TestMethod]
        public void TestBindToIBinder()
        {
            var account = TestStorage.GetAccount();

            BlobClient.DeleteContainer(account, "daas-test-input");

            var lc = TestStorage.New<Program>(account);
            lc.Call("TestBinder");                        

            string content = BlobClient.ReadBlob(account, "daas-test-input", "directout.txt");
            Assert.AreEqual("output", content);
        }

        [TestMethod]
        public void TestModelBinding()
        {
            var account = TestStorage.GetAccount();

            BlobClient.DeleteContainer(account, "daas-test-input");
            BlobClient.WriteBlob(account, "daas-test-input", "input.txt", "abc");

            var lc = TestStorage.New<Program>(account);
            IConfiguration config = lc.Configuration;
            config.BlobBinders.Add(new ModelBlobBinderProvider());
            lc.CallOnBlob("Func", @"daas-test-input\input.txt");

            string content = BlobClient.ReadBlob(account, "daas-test-input", "output.txt");
            Assert.AreEqual("*abc*", content);
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

            class ModelInputBlobBinder : ICloudBlobBinder
            {
                public BindResult Bind(IBinderEx bindingContext, string containerName, string blobName, Type targetType)
                {
                    CloudBlob blob = GetBlob(bindingContext.AccountConnectionString, containerName, blobName);

                    var content = blob.DownloadText();
                    return new BindResult { Result = new Model { Value = content }  };
                }
            }

            class ModelOutputBlobBinder : ICloudBlobBinder
            {
                public BindResult Bind(IBinderEx bindingContext, string containerName, string blobName, Type targetType)
                {
                    CloudBlob blob = GetBlob(bindingContext.AccountConnectionString, containerName, blobName);

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

            public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
            {
                if (targetType == typeof(Model))
                {
                    if (isInput)
                    {
                        return new ModelInputBlobBinder();
                    }
                    else
                    {
                        return new ModelOutputBlobBinder();
                    }
                }
                return null;
            }

            private static CloudBlob GetBlob(string accountConnectionString, string containerName, string blobName)
            {
                var account = Utility.GetAccount(accountConnectionString);
                var client = account.CreateCloudBlobClient();
                var c = client.GetContainerReference(containerName);
                var blob = c.GetBlobReference(blobName);
                return blob;
            }
        }

        class Program
        {
            [Jobs.Description("Invoke with an IBinder")]
            public static void TestBinder(IBinder binder)
            {
                TextWriter tw = binder.BindWriteStream<TextWriter>("daas-test-input", "directout.txt");
                tw.Write("output");

                // closed automatically 
            }


            public static void Func(
                [BlobInput(@"daas-test-input\input.txt")] Model input,
                [BlobOutput(@"daas-test-input\output.txt")] out Model output)
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
