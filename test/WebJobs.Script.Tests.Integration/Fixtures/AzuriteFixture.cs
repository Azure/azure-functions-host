using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using System.Threading;
using Xunit.Sdk;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.Fixtures
{
    public class AzuriteFixture(IMessageSink sink) : IAsyncLifetime
    {
        public const string HostName = "127.0.0.1";

        public const string AccountName = "devstoreaccount1";

        [SuppressMessage("Microsoft.Security", "CS002:SecretInNextLine", Justification = "Well known account key for emulator. Used for testing.")]
        public const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        private int _blobPort;
        private int _queuePort;
        private int _tablePort;

        private Process _process;
        private ExceptionDispatchInfo _exceptionDispatchInfo;

        public string GetConnectionString()
        {
            VerifyInitialized();
            IEnumerable<(string Key, string Value)> properties =
                [
                    ("DefaultEndpointsProtocol", Uri.UriSchemeHttps),
                    ("AccountName", AccountName),
                    ("AccountKey", AccountKey),
                    ("BlobEndpoint", GetBlobEndpoint()),
                    ("QueueEndpoint", GetQueueEndpoint()),
                    ("TableEndpoint", GetTableEndpoint()),
                ];

            return string.Join(";", properties.Select(p => $"{p.Key}={p.Value}"));
        }

        /// <summary>
        /// Gets the blob endpoint
        /// </summary>
        /// <returns>The Azurite blob endpoint</returns>
        public string GetBlobEndpoint()
        {
            VerifyInitialized();
            return new UriBuilder(Uri.UriSchemeHttp, HostName, _blobPort, AccountName).ToString();
        }

        /// <summary>
        /// Gets the queue endpoint
        /// </summary>
        /// <returns>The Azurite queue endpoint</returns>
        public string GetQueueEndpoint()
        {
            VerifyInitialized();
            return new UriBuilder(Uri.UriSchemeHttp, HostName, _queuePort, AccountName).ToString();
        }

        /// <summary>
        /// Gets the table endpoint
        /// </summary>
        /// <returns>The Azurite table endpoint</returns>
        public string GetTableEndpoint()
        {
            VerifyInitialized();
            return new UriBuilder(Uri.UriSchemeHttp, HostName, _tablePort, AccountName).ToString();
        }

        public async Task InitializeAsync()
        {
            try
            {
                await StartAzuriteAsync();
            }
            catch (Exception ex)
            {
                _exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);
            }
        }

        public async Task DisposeAsync()
        {
            if (Interlocked.Exchange(ref _process, null) is { } p)
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }

                using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
                await p.WaitForExitAsync(cts.Token);

                p.Dispose();
            }
        }

        private static int GetFreeTcpPort()
        {
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }

        private Task StartAzuriteAsync()
        {
            _blobPort = GetFreeTcpPort();
            _queuePort = GetFreeTcpPort();
            _tablePort = GetFreeTcpPort();
            GetAzuriteCommand(out string process, out string arguments);
            ProcessStartInfo startInfo = new()
            {
                FileName = process,
                Arguments = arguments,
                UseShellExecute = false, // we need stdio, cannot set to true.
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                ErrorDialog = false,
            };

            // We consider this startup complete when the first message is written.
            // stdout: successfully started.
            // stderr: failed to start.
            TaskCompletionSource tcs = new();
            _process = new() { StartInfo = startInfo };
            _process.ErrorDataReceived += (_, e) => OnError(e, tcs);
            _process.OutputDataReceived += (_, e) => OnMessage(e, tcs);
            _process.Start();
            _process.BeginErrorReadLine();
            _process.BeginOutputReadLine();
            return tcs.Task;
        }

        private void GetAzuriteCommand(out string process, out string arguments)
        {
            // Azurite is not an executable itself, but a node module. However, it installs helper scripts on the path.
            // We will use cmd or bash to run the helper script.
            string azurite = $"azurite --silent --inMemoryPersistence --blobPort {_blobPort} --queuePort {_queuePort} --tablePort {_tablePort}";
            if (OperatingSystem.IsWindows())
            {
                process = "cmd";
                arguments = $"/C {azurite}";
            }
            else
            {
                process = "bash";
                arguments = $"-c \"{azurite}\"";
            }
        }

        private void OnMessage(DataReceivedEventArgs e, TaskCompletionSource tcs)
        {
            if (e.Data is not null)
            {
                tcs.TrySetResult();
                sink.OnMessage(new DiagnosticMessage($"[azurite] {e.Data}"));
            }
        }

        private void OnError(DataReceivedEventArgs e, TaskCompletionSource tcs)
        {
            if (e.Data is null)
            {
                return;
            }

            sink.OnMessage(new DiagnosticMessage($"[error][azurite] {e.Data}"));
            try
            {
                throw new InvalidOperationException(e.Data);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private void VerifyInitialized()
        {
            if (_process is null)
            {
                throw new InvalidOperationException("AzuriteFixture is not initialized. Call InitializeAsync() before using this fixture.");
            }

            _exceptionDispatchInfo?.Throw();
        }
    }
}
