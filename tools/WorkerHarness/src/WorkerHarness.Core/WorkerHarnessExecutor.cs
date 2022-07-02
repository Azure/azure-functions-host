// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Grpc.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace WorkerHarness.Core
{
    public class DefaultWorkerHarnessExecutor : IWorkerHarnessExecutor
    {
        private readonly HarnessOptions _workerDescription;
        private readonly IWorkerProcessBuilder _workerProcessBuilder;
        private readonly IScenarioParser _scenarioParser;
        private readonly ILogger<DefaultWorkerHarnessExecutor> _logger;

        public DefaultWorkerHarnessExecutor(IOptions<HarnessOptions> workerDescription,
            IWorkerProcessBuilder workerProcessBuilder,
            IScenarioParser scenarioParser,
            ILogger<DefaultWorkerHarnessExecutor> logger)
        {
            _workerDescription = workerDescription.Value;
            _workerProcessBuilder = workerProcessBuilder;
            _scenarioParser = scenarioParser;
            _logger = logger;
        }

        public async Task<bool> Start()
        {

            Process myProcess = _workerProcessBuilder.Build(_workerDescription);

            string scenarioFile = _workerDescription.ScenarioFile ?? throw new ArgumentException("missing the scenario file");

            Scenario scenario = _scenarioParser.Parse(scenarioFile);

            try
            {
                myProcess.Start();

                foreach (IAction action in scenario.Actions)
                {
                    await action.ExecuteAsync();
                    
                }

                myProcess.Kill();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n\n{ex}");

                return false;
            }
        }
    }
}
