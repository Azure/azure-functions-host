using System.ComponentModel.DataAnnotations;

namespace Dashboard.ViewModels
{
    public class PagingInfo
    {
        public string OlderThan { get; set; }
        public string OlderThanOrEqual { get; set; }
        public string NewerThan { get; set; }
        [Range(1, 100)]
        public int? Limit { get; set; }
    }
}