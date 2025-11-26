// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    internal static class WorkerUtilities
    {
        public static int GetUnusedTcpPort()
        {
            using (Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                tcpSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                int port = ((IPEndPoint)tcpSocket.LocalEndPoint).Port;
                return port;
            }
        }

        /// <summary>
        /// Determines whether the specified port is available.
        /// </summary>
        internal static bool CanBindToPort(int port)
        {
            // Try to bind to the port using IPv6 dual mode socket to cover both IPv4 and IPv6.
            using var tcpSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp) { DualMode = true };
            try
            {
                tcpSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
                return true;
            }
            catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                return false;
            }
            catch
            {
                // Fall back to IPv4 only socket if IPv6 is not supported on the platform.
                using var tcpSocketAny = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    tcpSocketAny.Bind(new IPEndPoint(IPAddress.Any, port));
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}