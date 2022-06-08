using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace WorkerHarness.Core.Worker
{
    public class WorkerDescriptionBuilder : IWorkerDescriptionBuilder
    {
        /// <summary>
        /// Create an instance of WorkerDescription from a worker.config.json file
        /// </summary>
        /// <param name="workerConfigPath">the absolute path to a worker.config.json file</param>
        /// <param name="workerDirectory">the absolute path of a worker directory</param>
        /// <returns> an instance of WorkerDescription class</returns>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="JsonException"></exception>
        public WorkerDescription Build(string workerConfigPath, string workerDirectory)
        {
            if (!File.Exists(workerConfigPath))
            {
                throw new FileNotFoundException($"The file {workerConfigPath} is not found");
            }

            string json = File.ReadAllText(workerConfigPath);
            JsonNode document = JsonNode.Parse(json)!;
            JsonNode jsonDescription = document["description"]!;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var deserializedObject = JsonSerializer.Deserialize(jsonDescription, typeof(WorkerDescription), options);

            WorkerDescription workerDescription;
            if (deserializedObject != null)
            {
                workerDescription = (WorkerDescription)deserializedObject;
            }
            else
            {
                throw new JsonException($"Unable to deserialize a {typeof(JsonNode)} to a {typeof(WorkerDescription)} object");
            }

            workerDescription.WorkerDirectory = workerDirectory;

            workerDescription.UseAbsolutePaths();

            return workerDescription;
        }
    }
}
