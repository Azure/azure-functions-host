using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestEnvironment : IEnvironment
    {
        private readonly IDictionary<string, string> _variables;

        public TestEnvironment()
            :this(new Dictionary<string, string>())
        {
        }

        public TestEnvironment(IDictionary<string, string> variables)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        }

        public string GetEnvironmentVariable(string name)
        {
            _variables.TryGetValue(name, out string result);

            return result;
        }

        public void SetEnvironmentVariable(string name, string value)
        {
            _variables[name] = value;
        }

        public static TestEnvironment FromEnvironmentVariables()
        {
            var variables = Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value, StringComparer.OrdinalIgnoreCase);

            return new TestEnvironment(variables);
        }
    }
}
