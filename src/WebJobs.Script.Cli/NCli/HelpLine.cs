using System.Diagnostics;

namespace NCli
{
    public class Helpline
    {
        public string Value { get; set; }
        public TraceLevel Level { get; set; }

        public override string ToString()
        {
            return Value;
        }
    }
}
