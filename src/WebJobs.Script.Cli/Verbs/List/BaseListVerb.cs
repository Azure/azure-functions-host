using WebJobs.Script.Cli.Interfaces;

namespace WebJobs.Script.Cli.Verbs.List
{
    internal abstract class BaseListVerb : BaseVerb
    {
        public BaseListVerb(ITipsManager tipsManager) : base(tipsManager)
        {
        }
    }
}
