namespace WebJobs.Script.ConsoleHost.Arm.Models
{
    public class ArmArrayWrapper<T>
    {
        public ArmWrapper<T>[] value { get; set; }
    }
}