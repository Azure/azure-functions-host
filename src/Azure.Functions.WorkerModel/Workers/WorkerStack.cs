namespace OutOfProcModel.Abstractions.ControlPlane;

public record WorkerStack(string Runtime, string Version, string Architecture, bool IsPlaceholder)
{
    public override string ToString()
    {
        return $"{Runtime} | {Version} | {Architecture}";
    }
}