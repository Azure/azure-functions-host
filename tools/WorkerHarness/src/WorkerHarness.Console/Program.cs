// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Options;
using WorkerHarness.Core;

namespace WorkerHarness
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var workerDirectory = "C:\\temp\\FunctionApp1\\FunctionApp1\\bin\\Debug\\net6.0";
            var workerFile = @"C:\temp\FunctionApp1\FunctionApp1\bin\Debug\net6.0\FunctionApp1.dll";
            var scenarioFile = @"C:\Dev\azure-functions-host\tools\WorkerHarness\src\WorkerHarness.Core\DefaultScenario.json";
            var language = "dotnet-isolated";

            IOptions<WorkerDescription> workerDescription = Options.Create(new WorkerDescription()
            {
                DefaultExecutablePath = Path.Combine(WorkerConstants.ProgramFilesFolder, WorkerConstants.DotnetFolder, WorkerConstants.DotnetExecutableFileName),
                DefaultWorkerPath = workerFile,
                WorkerDirectory = workerDirectory,
                Language = language
            });

            IWorkerProcessBuilder workerProcessBuilder = new WorkerProcessBuilder();

            IGrpcMessageProvider rpcMessageProvider = new GrpcMessageProvider(workerDescription);

            IValidatorManager validatorManager = new ValidatorManager();

            IActionProvider actionProvider = new DefaultActionProvider(validatorManager, rpcMessageProvider);

            IScenarioParser scenarioParser = new ScenarioParser(actionProvider);

            WorkerHarnessExecutor harnessExecutor = new WorkerHarnessExecutor(workerDescription, workerProcessBuilder, scenarioParser);

            harnessExecutor.Start(scenarioFile);
        }
    }
}
