using System.ComponentModel.DataAnnotations;

namespace Dashboard.ViewModels
{
    public class ContainerPagingInfo
    {
        [Range(1, 100)]
        public int Limit { get; set; }
        public string ContinuationToken { get; set; }
    }
}
