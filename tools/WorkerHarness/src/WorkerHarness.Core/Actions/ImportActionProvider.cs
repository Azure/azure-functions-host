// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

namespace WorkerHarness.Core.Actions
{
    public class ImportActionProvider : IActionProvider
    {
        public string Type => ActionTypes.Import;

        internal static string MissingScenarioFileException = "The import action is missing a scenarioFile property";
        internal static string ScenarioFileDoesNotExist = "The scenario file {0} does not exist";

        private readonly ILoggerFactory _loggerFactory;

        public ImportActionProvider(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory;
        }

        public IAction Create(JsonNode actionNode)
        {
            try
            {
                TryGetScenarioFile(out string scenarioFile, actionNode);

                return new ImportAction(scenarioFile, _loggerFactory.CreateLogger<ImportAction>());
            }
            catch (ArgumentException ex)
            {
                throw ex;
            }

        }

        private static bool TryGetScenarioFile(out string scenarioFile, JsonNode actionNode)
        {
            if (actionNode["scenarioFile"] == null)
            {
                throw new ArgumentException(MissingScenarioFileException);
            }

            scenarioFile = actionNode["scenarioFile"]!.GetValue<string>();
            scenarioFile = Path.GetFullPath(scenarioFile);

            if (!File.Exists(scenarioFile))
            {
                throw new ArgumentException(ScenarioFileDoesNotExist, scenarioFile);
            }

            return true;
        }
    }
}
