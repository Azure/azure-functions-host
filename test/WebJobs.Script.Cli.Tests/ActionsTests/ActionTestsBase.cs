using System;
using System.IO;
using Xunit.Abstractions;

namespace WebJobs.Script.Cli.Tests.ActionsTests
{
    public abstract class ActionTestsBase : IDisposable
    {
        protected ITestOutputHelper Output { get; private set; }

        protected string WorkingDirectory { get; private set; }

        protected ActionTestsBase(ITestOutputHelper output)
        {
            Output = output;
            WorkingDirectory = Path.GetTempFileName();
            CleanUp(WorkingDirectory);
            Directory.CreateDirectory(WorkingDirectory);
            Environment.CurrentDirectory = WorkingDirectory;
        }

        public void Dispose()
        {
            CleanUp(WorkingDirectory);
        }

        private void CleanUp(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Output.WriteLine(ex.ToString());
            }
        }
    }
}