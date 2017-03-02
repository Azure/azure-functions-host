// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.Dispatch
{
    internal class LanguageWorkerChannel : ILanguageWorkerChannel
    {
        public ChannelState State { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public Task<object> Invoke(object[] parameters)
        {
            throw new NotImplementedException();
        }

        public Task Load(FunctionMetadata functionMetadata)
        {
            throw new NotImplementedException();
        }

        public Task Start()
        {
            return Task.Run(() => StartProcess(@"node.exe", ".\nodejsWorker.js", "."));
        }

        /* public Task<LanguageInvokerInitializationResult> SetupNodeRpcWorker(TraceWriter systemTraceWriter)
        // {
        //    // Any setup needed for GRPC
        //    // systemTraceWriter.Info("SetupNodeRpcWorker...");
        //    StartProcess(@"node.exe", ".\nodejsWorker.js", ".");
        //    bool tcpEndpointAvailable = Utilities.IsTcpEndpointAvailable(RpcConstants.RpcWorkerHost, RpcConstants.NodeRpcWorkerPort, systemTraceWriter);
        //    if (!tcpEndpointAvailable)
        //    {
        //        // string fileName = @"C:\Program Files (x86)\nodejs\node.exe";
        //        // on Azure
        //        string fileName = @"node.exe";

        //        // string arguments = @" E:\FuncLang\grpc\examples\node\dynamic_codegen\route_guide\nodeRpcWorker.js";
        //        // on Azure
        //        string arguments = @"D:\home\SiteExtensions\Functions\bin\nodejsWorker.js";
        //        _nodeRpcWorker = Utilities.StartProcess(fileName, arguments, Path.GetDirectoryName(arguments), systemTraceWriter);
        //    }
        //    tcpEndpointAvailable = Utilities.IsTcpEndpointAvailable(RpcConstants.RpcWorkerHost, RpcConstants.NodeRpcWorkerPort, systemTraceWriter);
        //    if (!tcpEndpointAvailable)
        //    {
        //        throw new InvalidOperationException($"Unable to start NodeRpcWorker");
        //    }
        //    LanguageInvokerInitializationResult initResult = new LanguageInvokerInitializationResult();
        //    initResult.LanguageServiceCapability = new Dictionary<string, string>();
        //    initResult.LanguageServiceCapability.Add("Lang", "node");

        //    return Task.FromResult(initResult);
        // } */

        private static Process StartProcess(string fileName, string arguments, string workingDirectory)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.UseShellExecute = false;   // This is important
                psi.CreateNoWindow = true;     // This is what hides the command window.
                psi.FileName = fileName;
                psi.Arguments = arguments;
                psi.WorkingDirectory = workingDirectory;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;

                var serviceProcess = Process.Start(psi);
                System.Threading.Thread.Sleep(new TimeSpan(0, 0, 5));

                serviceProcess.WaitForExit();
                return serviceProcess;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
