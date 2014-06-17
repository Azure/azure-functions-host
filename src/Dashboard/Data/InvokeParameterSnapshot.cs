using Microsoft.Azure.Jobs.Protocols;

namespace Dashboard.Data
{
    [JsonTypeName("Invoke")]
    internal class InvokeParameterSnapshot : ParameterSnapshot
    {
        public override string Description
        {
            get { return "Caller-supplied value"; }
        }

        public override string Prompt
        {
            get { return "Enter a value"; }
        }

        public override string DefaultValue
        {
            get { return null; }
        }
    }
}
