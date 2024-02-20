// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core.Actions
{
    public sealed class ImportActionProvider : IActionProvider
    {
        public string Type => ActionTypes.Import;

        internal static string MissingScenarioFileException = "The import action is missing a scenarioFile property";
        internal static string ScenarioFileDoesNotExist = "The scenario file {0} does not exist";

        private readonly ILogger<ImportActionProvider> _logger;

        public ImportActionProvider(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory?.CreateLogger<ImportActionProvider>() ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public IAction Create(JsonNode actionNode)
        {
            try
            {
                TryGetScenarioFile(out string scenarioFile, actionNode);

                return new ImportAction(scenarioFile);
            }
            catch (ArgumentException)
            {
                throw;
            }
        }

        private bool TryGetScenarioFile(out string scenarioFile, JsonNode actionNode)
        {
            if (actionNode["scenarioFile"] == null)
            {
                throw new ArgumentException(MissingScenarioFileException);
            }

            scenarioFile = actionNode["scenarioFile"]!.GetValue<string>();
            scenarioFile = Path.GetFullPath(scenarioFile);

            bool fileExist = File.Exists(scenarioFile);
            
            if (!fileExist)
            {
                _logger.LogError($"Scenario file {scenarioFile} does not exist", scenarioFile);
                throw new ArgumentException(ScenarioFileDoesNotExist, scenarioFile);
            }

            return true;
        }
    }
}
