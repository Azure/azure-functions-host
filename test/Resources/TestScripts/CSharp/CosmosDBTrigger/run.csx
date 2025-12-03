#r "nuget: Microsoft.Azure.Cosmos"

public static void Run(IList<dynamic> input, out string completed)
{
    completed = input[0].id;
}