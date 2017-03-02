// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Rpc.Messages;
using WebJobs.Script.Rpc;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public static class Utilities
    {
        private static TraceWriter systemTraceWriter;

        private static void StandardOutputReceiver(object sendingProcess, DataReceivedEventArgs outLine)
        {
            // Receives the child process' standard output
            if (!string.IsNullOrEmpty(outLine.Data))
            {
                systemTraceWriter.Verbose("StandardOutputReceiver: " + outLine.Data);
            }
        }

        private static void StandardErrorReceiver(object sendingProcess, DataReceivedEventArgs errLine)
        {
            // Receives the child process' standard error
            if (!string.IsNullOrEmpty(errLine.Data))
            {
                systemTraceWriter.Verbose("StandardErrorReceiver: " + errLine.Data);
            }
        }

        public static bool IsTcpEndpointAvailable(string addressArgument, int portNumber, TraceWriter systemTraceWriter)
        {
            TcpClient tcpClient = null;
            bool endPointAvailable = false;
            try
            {
                // systemTraceWriter.Verbose($"Trying to connect to host:{addressArgument} on port:{portNumber}.");
                tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = tcpClient.SendTimeout = 2000;
                IPAddress address;
                if (IPAddress.TryParse(addressArgument, out address))
                {
                    systemTraceWriter.Verbose($"address {address} .");
                    var endPoint = new IPEndPoint(address, portNumber);
                    tcpClient.Connect(endPoint);
                }
                else
                {
                    tcpClient.Connect(addressArgument, portNumber);
                }

                systemTraceWriter.Verbose($"TCP connect succeeded. host:{addressArgument} on port:{portNumber}..");
                endPointAvailable = true;
            }
            catch (Exception e)
            {
                systemTraceWriter.Verbose(e.StackTrace);

                if (e is SocketException || e is TimeoutException)
                {
                    systemTraceWriter.Verbose($"Not listening on port {portNumber}.");
                }
            }
            finally
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
            }
            systemTraceWriter.Verbose($"endPointAvailable: {endPointAvailable}");
            return endPointAvailable;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public static Process StartProcess(string fileName, string arguments, string workingDirectory, TraceWriter systemTraceWriter)
        {
            Utilities.systemTraceWriter = systemTraceWriter;
            systemTraceWriter.Verbose($"Starting following process:\n fileName: {fileName}\n arguments:{arguments}\n workingDirectory:{workingDirectory}");
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
                serviceProcess.OutputDataReceived += new DataReceivedEventHandler(StandardOutputReceiver);
                serviceProcess.BeginOutputReadLine();
                serviceProcess.ErrorDataReceived += new DataReceivedEventHandler(StandardErrorReceiver);
                serviceProcess.BeginErrorReadLine();
                string stdoutx = serviceProcess.StandardOutput.ReadToEnd();
                Utilities.systemTraceWriter.Verbose("stdoutx: " + stdoutx);

                // TODO re-use src\WebJobs.Script\Description\Node\Compilation\TypeScript\TypeScriptCompilation.cs CompileAsync
                string stderrx = serviceProcess.StandardError.ReadToEnd();
                Utilities.systemTraceWriter.Verbose("stderrx: " + stderrx);

                // TODO subscribe to exit event
                serviceProcess.WaitForExit();
                systemTraceWriter.Verbose("Service Process ID: " + serviceProcess.Id);
                systemTraceWriter.Verbose("done...server..");
                return serviceProcess;
            }
            catch (Exception ex)
            {
                systemTraceWriter.Verbose(ex.Message);
                return null;
            }
        }

        public static void StartGoogleRpcServer(string googleRpcServerHost, string googleRpcServerPort)
        {
            // string port = Utility.GetSettingFromConfigOrEnvironment(RpcConstants.GoogleRpcServerPort);
            // string hostAddress = Utility.GetSettingFromConfigOrEnvironment(RpcConstants.GoogleRpcServerHost);

            if (string.IsNullOrEmpty(googleRpcServerPort))
            {
                throw new ArgumentNullException("googleRpcServerPort");
            }
            if (string.IsNullOrEmpty(googleRpcServerHost))
            {
                throw new ArgumentNullException("googleRpcServerHost");
            }

            Server server = new Server
            {
                Services = { FunctionRpc.BindService(new GoogleRpcServer()) },
                Ports = { new ServerPort(googleRpcServerHost, int.Parse(googleRpcServerPort), ServerCredentials.Insecure) }
            };
            Console.WriteLine($"Starting grpc service on port: {googleRpcServerPort}");
            server.Start();
            TcpClient tcpClient = null;
            try
            {
                Console.WriteLine("Trying to connect to host:{0} on port:{1}.", googleRpcServerHost, googleRpcServerPort);
                tcpClient = new TcpClient();
                tcpClient.ReceiveTimeout = tcpClient.SendTimeout = 2000;
                IPAddress address;
                if (IPAddress.TryParse(googleRpcServerHost, out address))
                {
                    var endPoint = new IPEndPoint(IPAddress.Loopback, int.Parse(googleRpcServerPort));
                    tcpClient.Connect(endPoint);
                }
                else
                {
                    tcpClient.Connect(googleRpcServerHost, int.Parse(googleRpcServerPort));
                }

                Console.WriteLine("TCP connect succeeded. host:{0} on port:{1}..", googleRpcServerHost, googleRpcServerPort);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Starting grpc service on port: {googleRpcServerPort} failed with error", ex);
            }
            finally
            {
                if (tcpClient != null)
                {
                    tcpClient.Close();
                }
            }

            Console.WriteLine("FunctionRpc server listening on port " + googleRpcServerPort);

            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();
            server.ShutdownAsync().Wait();
        }

        public static void KillProcess(Process serviceProcess)
        {
            serviceProcess.Kill();
        }
    }
}
