using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkerHarness.Core
{
    /// <summary>
    /// A concrete implementation of the IComposingService interface
    /// </summary>
    public class ComposingService : IComposingService
    {
        public Compose Compose(string composeFile)
        {
            // read the composeFile and deserialize it to a list of ScenarioInstruction objects
            string fileContent = File.ReadAllText(composeFile);

            var options = new JsonSerializerOptions() 
            {
                PropertyNameCaseInsensitive = true
            };
            var execuationContext = JsonSerializer.Deserialize<Compose>(fileContent, options);
            if (execuationContext == null)
            {
                throw new InvalidOperationException($"Cannot deserialize the {composeFile} file");
            }

            // Iterate through all scenario paths, check if full path or relative path
            foreach (Instruction scenarioContext in execuationContext.Instructions)
            {
                string fullScenarioPath = GetFullPath(scenarioContext.Action);

                // check whether the file exists
                if (!File.Exists(fullScenarioPath))
                {
                    throw new FileNotFoundException($"Cannot find the {fullScenarioPath} file");
                }

                scenarioContext.Action = fullScenarioPath;
            }

            return execuationContext;
        }

        private static string GetFullPath(string scenarioName)
        {
            string qualifiedPath = scenarioName;

            // check if full path or relative. If relative, construct full path
            if (!Path.IsPathFullyQualified(scenarioName))
            {
                qualifiedPath = Path.Combine(Environment.CurrentDirectory, "CoreScenarios", scenarioName);
            }

            // if missing extension ".scenario", append it to the end
            string scenarioExtensionPattern = @".+\.scenario$";
            if (!Regex.IsMatch(qualifiedPath, scenarioExtensionPattern))
            {
                qualifiedPath += ".scenario";
            }

            return qualifiedPath;
        }
    }
}
