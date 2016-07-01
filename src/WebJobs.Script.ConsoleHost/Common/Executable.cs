using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebJobs.Script.ConsoleHost.Extensions;

namespace WebJobs.Script.ConsoleHost.Common
{
    internal class Executable
    {
        private string _arguments;
        private string _exeName;
        private bool _shareConsole;
        private bool _streamOutput;

        public Executable(string exeName, string arguments = null, bool streamOutput = true, bool shareConsole = false)
        {
            _exeName = exeName;
            _arguments = arguments;
            _streamOutput = streamOutput;
            _shareConsole = shareConsole;
        }

        public async Task RunAsync(Action<string> outputCallback = null, Action<string> errorCallback = null)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = _exeName,
                Arguments = _arguments,
                CreateNoWindow = true,
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
            await process.WaitForExitAsync();
        }
    }
}
