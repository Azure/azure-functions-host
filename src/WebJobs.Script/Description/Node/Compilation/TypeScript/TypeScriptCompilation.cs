// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompilation : IJavaScriptCompilation
    {
        private static readonly Regex DiagnosticRegex = new Regex("^(.*.ts)\\((\\d*),(\\d*)\\): (\\w*) (TS[0-9]{0,4}): (.*)$", RegexOptions.Compiled);
        private readonly string _inputFilePath;
        private readonly TypeScriptCompilationOptions _options;
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        private TypeScriptCompilation(string inputFilePath, TypeScriptCompilationOptions options)
        {
            _inputFilePath = inputFilePath;
            _options = options;
        }

        public bool SupportsDiagnostics => true;

        private Task CompileAsync()
        {
            var tcs = new TaskCompletionSource<object>();

            try
            {
                string inputDirectory = Path.GetDirectoryName(_inputFilePath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = _options.ToolPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = inputDirectory,
                    Arguments = _options.ToArgumentString(_inputFilePath)
                };

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += ProcessDataReceived;
                process.OutputDataReceived += ProcessDataReceived;
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    process.WaitForExit();
                    process.Close();

                    if (_diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        tcs.SetException(new CompilationErrorException("Compilation failed.", GetDiagnostics()));
                    }
                    else
                    {
                        tcs.SetResult(null);
                    }
                };

                process.Start();

                process.BeginErrorReadLine();
                process.BeginOutputReadLine();
            }
            catch (Exception exc)
            {
                tcs.SetException(exc);
            }

            return tcs.Task;
        }

        private void ProcessDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && TryParseDiagnostic(e.Data, out Diagnostic diagnostic))
            {
                _diagnostics.Add(diagnostic);
            }
        }

        internal static bool TryParseDiagnostic(string data, out Diagnostic diagnostic)
        {
            diagnostic = null;
            Match match = DiagnosticRegex.Match(data);

            if (match.Success)
            {
                DiagnosticSeverity severity = (DiagnosticSeverity)Enum.Parse(typeof(DiagnosticSeverity), match.Groups[4].Value, true);
                var descriptor = new DiagnosticDescriptor(match.Groups[5].Value, match.Groups[6].Value, match.Groups[6].Value, string.Empty, severity, true);
                int line = int.Parse(match.Groups[2].Value) - 1;
                int column = int.Parse(match.Groups[3].Value) - 1;
                var linePosition = new LinePosition(line, column);
                var location = Location.Create(match.Groups[1].Value, new TextSpan(linePosition.Line, 0), new LinePositionSpan(linePosition, linePosition));

                diagnostic = Diagnostic.Create(descriptor, location);
            }

            return diagnostic != null;
        }

        public static async Task<TypeScriptCompilation> CompileAsync(string inputFile, TypeScriptCompilationOptions options)
        {
            var compilation = new TypeScriptCompilation(inputFile, options);
            await compilation.CompileAsync();

            return compilation;
        }

        public ImmutableArray<Diagnostic> GetDiagnostics() => ImmutableArray.Create(_diagnostics.ToArray());

        object ICompilation.Emit(CancellationToken cancellationToken) => Emit(cancellationToken);

        public string Emit(CancellationToken cancellationToken)
        {
            string inputFilePath = Path.GetDirectoryName(_inputFilePath);
            string outputFileName = Path.GetFileNameWithoutExtension(_inputFilePath) + ".js";

            return Path.Combine(inputFilePath, _options.OutDir, outputFileName);
        }
    }
}
