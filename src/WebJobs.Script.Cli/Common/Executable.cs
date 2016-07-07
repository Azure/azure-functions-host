// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WebJobs.Script.Cli.Extensions;

namespace WebJobs.Script.Cli.Common
{
    internal class Executable
    {
        private string _arguments;
        private string _exeName;
        private bool _shareConsole;
        private bool _streamOutput;
        private readonly bool _visibleProcess;

        public Executable(string exeName, string arguments = null, bool streamOutput = true, bool shareConsole = false, bool visibleProcess = false)
        {
            _exeName = exeName;
            _arguments = arguments;
            _streamOutput = streamOutput;
            _shareConsole = shareConsole;
            _visibleProcess = visibleProcess;
        }

        public async Task<int> RunAsync(Action<string> outputCallback = null, Action<string> errorCallback = null)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = _exeName,
                Arguments = _arguments,
                CreateNoWindow = !_visibleProcess,
                UseShellExecute = _shareConsole,
                RedirectStandardError = _streamOutput,
                RedirectStandardInput = _streamOutput,
                RedirectStandardOutput = _streamOutput
            };

            var process = Process.Start(processInfo);

            if (_streamOutput)
            {
                if (outputCallback != null)
                {
                    process.OutputDataReceived += (s, e) => outputCallback(e.Data);
                    process.BeginOutputReadLine();
                }

                if (errorCallback != null)
                {
                    process.ErrorDataReceived += (s, e) => errorCallback(e.Data);
                    process.BeginErrorReadLine();
                }
                process.EnableRaisingEvents = true;
            }
            return await process.WaitForExitAsync();
        }
    }
}
