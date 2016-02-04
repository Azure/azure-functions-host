// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script;
using System.Threading;
using Microsoft.WindowsAzure.Storage.Blob;

namespace WebJobs.Script.Tests
{
    [Trait("Category", "E2E")]
    public class ScriptHostManagerTests 
    {
        // Update a script file (the function.json) to force the ScriptHost to re-index and pick up new changes. 
        // Test with timers: 
        [Fact]
        public async Task UpdateFileAndRestart()
        {
            Random r = new Random();

            CancellationTokenSource cts = new CancellationTokenSource();

            var fixture = new NodeEndToEndTests.TestFixture();
            var blob1 = UpdateOutputName("outputblobname", "first", fixture);

            await fixture.Host.StopAsync();
            var config = fixture.Host.ScriptConfig;            

            using (var manager = new ScriptHostManager(config))
            {
                // Background task to run while the main thread is pumping events at RunAndBlock(). 
                Thread t = new Thread(_ =>
                   {
                       // Wait for initial execution.
                       Wait(blob1);

                       // This changes the bindings so that we now write to blob2
                       var blob2 = UpdateOutputName("first", "second", fixture);

                       // wait for newly executed 
                       Wait(blob2);

                       manager.Stop();
                   });
                t.Start();                           

                manager.RunAndBlock(cts.Token);

                t.Join();
            }
        }

        // For a blob to appear. This is evidence that the function ran. 
        void Wait(CloudBlockBlob blob)
        {
            Stopwatch sw = Stopwatch.StartNew();
            const int timeoutMs = 10 * 1000; // 

            while (!blob.Exists())
            {
                Thread.Sleep(500);
                if (sw.ElapsedMilliseconds > timeoutMs)
                {
                    // If no blob appeared yet, then the function didn't run. 
                    // It may have not picked up the new changes
                    Assert.True(false, "Timeout waiting for blob to appear. " + timeoutMs + "ms");
                }
            }
        }

        // Update the manifest for the timer function
        // - this will cause a file touch which cause ScriptHostManager to notice and update
        // - set to a new output location so that we can ensure we're getting new changes. 
        static CloudBlockBlob UpdateOutputName(string prev, string hint, EndToEndTestFixture fixture)
        {
            string name = hint;

            string manifestPath = Path.Combine(Environment.CurrentDirectory, @"TestScripts\Node\TimerTrigger\function.json");
            string content = File.ReadAllText(manifestPath);            
            content = content.Replace(prev, name);
            File.WriteAllText(manifestPath, content);

            var blob = fixture.TestContainer.GetBlockBlobReference(name);
            blob.DeleteIfExists();
            return blob;

        }
    }
}