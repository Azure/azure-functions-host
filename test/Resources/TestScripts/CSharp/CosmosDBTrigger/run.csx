#r "Microsoft.Azure.DocumentDB.Core"

using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System;

public static void Run(IReadOnlyList<Document> input, out string completed)
{
    completed = input[0].Id;
}