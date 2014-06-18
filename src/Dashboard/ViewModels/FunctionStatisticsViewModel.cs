namespace Dashboard.ViewModels
{
    public class FunctionStatisticsViewModel
    {
        public string FunctionId { get; set; }
        public string FunctionFullName { get; set; }
        public string FunctionName { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public bool IsRunning { get; set; }
    }
}
