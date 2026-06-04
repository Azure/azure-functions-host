using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class CSharpPrecompiledEndToEndTestFixture : EndToEndTestFixture
    {
        private const string TestPathTemplate = "..\\..\\{0}\\{1}";

        private readonly IDisposable _dispose;

        public CSharpPrecompiledEndToEndTestFixture(string testProjectName, IDictionary<string, string> envVars = null, string functionWorkerRuntime = "dotnet")
            : base(string.Format(TestPathTemplate, testProjectName, TestHelpers.BuildConfig), testProjectName, functionWorkerRuntime)
        {
            if (envVars != null)
            {
                _dispose = new TestScopedEnvironmentVariable(envVars);
            }
        }

        protected override Task CreateTestStorageEntities()
        {
            return Task.CompletedTask;
        }

        public override async Task DisposeAsync()
        {
            _dispose?.Dispose();

            try
            {
                if (Host.WebHost is not null)
                {
                    await Host.WebHost.StopAsync();
                    Host.WebHost.Dispose();
                }
            }
            finally
            {
                await base.DisposeAsync();
            }
        }
    }
}
