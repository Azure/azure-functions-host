// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.Azure.WebJobs.Script.Description.Node.TypeScript
{
    public class TypeScriptCompiler : ITypeScriptCompiler
    {
        private static readonly Regex DiagnosticRegex = new Regex("^(.*.ts)\\((\\d*),(\\d*)\\): (\\w*) (TS[0-9]{0,4}): (.*)$", RegexOptions.Compiled);

        public Task<ImmutableArray<Diagnostic>> CompileAsync(string inputFile, TypeScriptCompilationOptions options)
        {
            var tcs = new TaskCompletionSource<ImmutableArray<Diagnostic>>();

            try
            {
                var diagnostics = new List<Diagnostic>();
                string inputDirectory = Path.GetDirectoryName(inputFile);

                var startInfo = new ProcessStartInfo
                {
                    FileName = options.ToolPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    ErrorDialog = false,
                    WorkingDirectory = inputDirectory,
                    Arguments = options.ToArgumentString(inputFile)
                };

                void ProcessDataReceived(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null && TryParseDiagnostic(e.Data, out Diagnostic diagnostic))
                    {
                        diagnostics.Add(diagnostic);
                    }
                }

                var process = new Process { StartInfo = startInfo };
                process.ErrorDataReceived += ProcessDataReceived;
                process.OutputDataReceived += ProcessDataReceived;
                process.EnableRaisingEvents = true;
                process.Exited += (s, e) =>
                {
                    process.WaitForExit();
                    process.Close();

                    var diagnosticResult = ImmutableArray.Create(diagnostics.ToArray());
                    if (diagnosticResult.Any(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        tcs.SetException(new CompilationErrorException("Compilation failed.", diagnosticResult));
                    }
                    else
                    {
                        tcs.SetResult(diagnosticResult);
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
    }
}
