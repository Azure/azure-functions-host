using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RunnerInterfaces;
using Microsoft.WindowsAzure.StorageClient;
using System.Reflection;
using Orchestrator;
using SimpleBatch;
using System.IO;

namespace OrchestratorUnitTests
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

            Utility.DeleteContainer(account, "daas-test-input");

            var lc = new LocalExecutionContext(account, typeof(Program));
            lc.Call("TestBinder");                        

            string content = Utility.ReadBlob(account, "daas-test-input", "directout.txt");
            Assert.AreEqual("output", content);
        }

        [TestMethod]
        public void TestModelBinding()
        {
            var account = TestStorage.GetAccount();

            Utility.DeleteContainer(account, "daas-test-input");
            Utility.WriteBlob(account, "daas-test-input", "input.txt", "abc");

            var lc = new LocalExecutionContext(account, typeof(Program));
            IConfiguration config = lc.Configuration;
            config.BlobBinders.Add(new ModelBlobBinderProvider());
            lc.CallOnBlob("Func", @"daas-test-input\input.txt");

            string content = Utility.ReadBlob(account, "daas-test-input", "output.txt");
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
            [SimpleBatch.Description("Invoke with an IBinder")]
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
