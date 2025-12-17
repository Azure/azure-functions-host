namespace OutOfProcModel.Abstractions.Core;

public interface IEventProcessor
{
    ValueTask<EventResult> ProcessEvent(EventContext context);
}

public class EventResult(InvocationResult invocationResult, string workerId)
{
    public InvocationResult InvocationResult { get; set; } = invocationResult;

    public string WorkerId { get; set; } = workerId;
}

public class EventContext(string applicationId, InvocationContext invocationContext)
{
    public string ApplicationId { get; set; } = applicationId;

    public InvocationContext InvocationContext { get; set; } = invocationContext;
}

public class InvocationContext(string invocationId, string data)
{
    public string InvocationId { get; set; } = invocationId;

    public string Data { get; set; } = data;
}

public class InvocationResult(string invocationId, string result)
{
    public string InvocationId { get; set; } = invocationId;

    public string Result { get; set; } = result;
}