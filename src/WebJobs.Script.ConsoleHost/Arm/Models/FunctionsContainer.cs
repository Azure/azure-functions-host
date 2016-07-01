using Newtonsoft.Json;
using System.Collections.Generic;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class FunctionsContainer
    {
        public string ScmUrl { get; set; }
        public string BasicAuth { get; set; }
        public string ArmId { get; set; }
        public Dictionary<string, string> AppSettings { get; set; }
    }
}