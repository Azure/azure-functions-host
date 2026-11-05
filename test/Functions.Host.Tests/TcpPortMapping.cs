// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Host.Tests;

/// <summary>
/// Maps an allocated external TCP port to a live listener without terminating TLS.
/// </summary>
internal sealed class TcpPortMapping : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _connections = [];
    private readonly Uri _target;
    private readonly Task _accept;

    public TcpPortMapping(Uri target)
    {
        _target = target;
        _listener.Start();
        Endpoint = new UriBuilder(target) { Port = ((IPEndPoint)_listener.LocalEndpoint).Port }.Uri;
        _accept = AcceptAsync();
    }

    public Uri Endpoint { get; }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                TcpClient incoming = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(ForwardAsync(incoming));
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (SocketException) when (_stop.IsCancellationRequested)
        {
        }
    }

    private async Task ForwardAsync(TcpClient incoming)
    {
        using (incoming)
        using (TcpClient outgoing = new())
        using (CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            try
            {
                await outgoing.ConnectAsync(_target.Host, _target.Port, connection.Token);
                Task toTarget = CopyAsync(incoming.GetStream(), outgoing.GetStream(), connection.Token);
                Task toCaller = CopyAsync(outgoing.GetStream(), incoming.GetStream(), connection.Token);
                try
                {
                    await await Task.WhenAny(toTarget, toCaller);
                }
                finally
                {
                    await connection.CancelAsync();
                    await Task.WhenAll(toTarget, toCaller);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }
    }

    private static async Task CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
