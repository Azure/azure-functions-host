using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    internal static class ProcessHelper
    {
        // Helper object used in client app to communicate with ProcessExecute<TInput, TResult>
        internal class ProcessExecuteArgs<TInput, TResult>
        {
            private readonly string[] _args;

            public ProcessExecuteArgs(string[] args)
            {
                _args = args;

                string json = File.ReadAllText(args[0]);
                this.Input = JsonCustom.DeserializeObject<TInput>(json);
            }

            public TInput Input { get; private set; }
            public TResult Result
            {
                set
                {
                    string outFile = _args[1];
                    string json = JsonCustom.SerializeObject(value);
                    File.WriteAllText(outFile, json);
                }
            }
        }

        // For debugging, allow launching in the same process. 
        // This is not correct because it loses process isolation, 
        // but can be very useful for debugging startup bugs in the target processes.
        // Having the switch as a static field also enables you to set-next-statement to the in-proc case while debugging.
        public static bool DebugRunInProc = false;

        // Invoke Main() in the target assembly. 
        // Sends in and received structured data. Use JSON.Net to serialize.
        // var path = new Uri(typeof(IndexDriver.Program).Assembly.CodeBase).LocalPath;
        public static TResult ProcessExecute<TInput, TResult>(Type targetType, string localCache, TInput input, TextWriter output)
        {
            return ProcessExecute<TInput, TResult>(targetType, localCache, input, output, CancellationToken.None);
        }

        public static TResult ProcessExecute<TInput, TResult>(Type targetType, string localCache, TInput input, TextWriter output, CancellationToken token)
        {
            if (!Directory.Exists(localCache))
            {
                Directory.CreateDirectory(localCache);
            }

            Assembly target = targetType.Assembly;

            string json = JsonCustom.SerializeObject(input);

            string inputPath = Path.Combine(localCache, "input.txt");
            string outputPath = Path.Combine(localCache, "output.txt");

            File.WriteAllText(inputPath, json);

            var path = new Uri(target.CodeBase).LocalPath;
            int processExitCode = 0;

            if (!DebugRunInProc)
            {
                string args = string.Format("\"{0}\" \"{1}\"", inputPath, outputPath);
                processExitCode = RunInSeparateProcess(path, args, output, token);
            }
            else
            {
                var args = new string[] { inputPath, outputPath };
                RunInAppDomain(path, args, output);
                // RunInSameDomain(targetType, args, output);
            }
            output.Flush();

            // If output path doesn't exist, then the target app crashed with a critical and unexpectd error. 
            // Normally, app should catch any user errors and propagate those results to the result object.
            if (!File.Exists(outputPath))
            {
                string msg = string.Format("Critical error: App {0} exited unexpectedly with exitcode {1} ({1:X}). See console output log for details.", target.FullName, processExitCode);
                throw new AbnormalTerminationException(msg);
            }

            string jsonResult = File.ReadAllText(outputPath);
            TResult result = JsonCustom.DeserializeObject<TResult>(jsonResult);

            return result;
        }

        // Run in the same appdomain, via reflection. 
        public static void RunInSameDomain(Type targetType, string[] args, TextWriter output)
        {
            var old = Console.Out;
            try
            {
                Console.SetOut(output);
                var mi = targetType.GetMethod("Main", new Type[] { typeof(string[]) });
                mi.Invoke(null, new object[] 
                        { 
                            args
                        });
            }
            catch (Exception e)
            {
                // Error
                Console.WriteLine("Error!! {0}", e.Message);
            }
            Console.SetOut(old); // restore
        }

        // Run in a isolated appdomain. Still in the same process (good for debugging and for security permissions), 
        // but also in a separate domain that we can unload after running. 
        public static void RunInAppDomain(string path, string[] args, TextWriter output)
        {
            var old = Console.Out;
            Console.SetOut(output);

            AppDomain domain = AppDomain.CreateDomain("second");

            RedirectConsoleOutput(domain, path, output);

            try
            {
                try
                {
                    domain.ExecuteAssembly(path, args);
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }
            catch (Exception e)
            {
                // Error
                Console.WriteLine("Error!! {0}", e.Message);
            }
            Console.SetOut(old); // restore
        }

        [DebuggerNonUserCode]
        private static void RedirectConsoleOutput(AppDomain domain, string path, TextWriter output)
        {
            // New appdomain does not inherit Console.Out
            // So we we have to invoke into the AppDomain and call Console.SetOut()
            // TextWriter marshal by ref, so we can still pass it in. 
            try
            {
                var objHandle = domain.CreateInstanceFrom(path, "RunnerHost.OutputSetter");
                var obj = objHandle.Unwrap();
                var flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.InvokeMethod;
                obj.GetType().InvokeMember("SetOut", flags, null, obj, new object[] { output });
            }
            catch
            {
                Console.WriteLine("Failed to capture child process console output");
            }
        }

        // redirect console.output to capture.
        // output stream is updated live (no buffering). 
        // return process exit code 
        private static int RunInSeparateProcess(string filename, string args, TextWriter output, CancellationToken token)
        {
            ProcessStartInfo si = new ProcessStartInfo();
            si.CreateNoWindow = true;
            si.UseShellExecute = false;
            si.FileName = filename;
            si.Arguments = args;
            si.RedirectStandardOutput = true;
            si.RedirectStandardError = true;

            // See here for details on capturing output
            // http://msdn.microsoft.com/en-us/library/system.diagnostics.process.beginoutputreadline.aspx

            object @lock = new object();

            Stopwatch sw = Stopwatch.StartNew();
            Process p = Process.Start(si);

            var funcOutput = new DataReceivedEventHandler(
                (sender, e) =>
                {
                    lock (@lock)
                    {
                        // Callback may occur on a separate thread. 
                        // data is batched in lines, strips the newline. Need to re-add.
                        string val = e.Data;
                        output.WriteLine(val);
                    }
                });

            p.ErrorDataReceived += funcOutput;
            p.OutputDataReceived += funcOutput;

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            try
            {
                while (!p.WaitForExit(1000))
                {
                    if (token.IsCancellationRequested)
                    {
                        p.Kill();
                        p.WaitForExit(); // make sure it really has exited.
                        output.WriteLine("Operation Cancelled");
                        throw new OperationCanceledException(token);
                    }
                }
            }
            finally
            {
                sw.Stop();
                lock (@lock)
                {
                    output.WriteLine("------");
                    output.WriteLine("Elappsed time:{0}s", sw.ElapsedMilliseconds / 1000.0);
                    output.WriteLine();

                    output.Flush();
                }
            }
            return p.ExitCode;
        }
    }
}
