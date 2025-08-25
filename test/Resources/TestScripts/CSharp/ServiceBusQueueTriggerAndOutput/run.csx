public static void Run(string trigger, out string output, out string completed)
{
    output = trigger + "-completed";
    completed = output;
}