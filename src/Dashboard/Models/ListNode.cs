using Dashboard.Models.Protocol;

namespace Dashboard.Controllers
{
    // Convert tree into flat list so that it's easier to render
    public class ListNode
    {
        public ExecutionInstanceLogEntityModel Func { get; set; }

        public int Depth { get; set; }
    }
}
