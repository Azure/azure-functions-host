using System.Collections.Generic;

namespace NCli
{
    public class HelpTextCollection : List<Helpline>
    {
        public HelpTextCollection(IEnumerable<Helpline> lines) : base(lines)
        {
        }
    }
}
