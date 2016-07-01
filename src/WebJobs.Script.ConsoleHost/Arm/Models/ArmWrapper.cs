using System.Collections.Generic;

namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class ArmWrapper<T>
    {
        public string id { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public string location { get; set; }
        public string kind { get; set; }
        public Dictionary<string, string> tags { get; set; }
        public T properties { get; set; }
    }
}