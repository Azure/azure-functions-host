using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Tests.Integration.WebHostEndToEnd
{
    public class CSharpPrecompiledEndToEndTestFixture : EndToEndTestFixture
    {
        private const string TestPathTemplate = "..\\..\\..\\..\\CSharpPrecompiledTestProjects\\{0}\\bin\\Debug\\netcoreapp3.1";
        private readonly IDisposable _dispose;

        public CSharpPrecompiledEndToEndTestFixture(string testProjectName, IDictionary<string, string> envVars = null, string functionWorkerRuntime = "dotnet")
            : base(string.Format(TestPathTemplate, testProjectName), testProjectName, functionWorkerRuntime)
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

        public override Task DisposeAsync()
        {
            _dispose?.Dispose();
            return base.DisposeAsync();
        }
    }
}
