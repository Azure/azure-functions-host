// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Net.Sockets;

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public static class WorkerUtilities
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
            using (var tcpSocket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
            {
                DualMode = true
            })
            {
                try
                {
                    tcpSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));

                    using (var tcpSocketAny = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                    {
                        tcpSocketAny.Bind(new IPEndPoint(IPAddress.Any, port));
                        return true;
                    }
                }
                catch (SocketException se) when (se.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    return false;
                }
            }
        }
    }
}