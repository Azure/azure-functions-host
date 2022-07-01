Worker Harness is a tool that validates a scenario against a language worker. Language worker developers can leverage this tool to test their isolated language model end-to-end, eliminating the need to spin up a host process.

# Run Worker Harness
Worker Harness is a Console Application. Clone the [Azure/azure-functions-host](https://github.com/Azure/azure-functions-host/) repos to your local machine and open it in Terminal or Command Prompt. Then use the `cd .\tools\WorkerHarness\` command to open the *WorkderHarness* folder.

## Command Line Arguments
```
--scenarioFile: the full path to a scenario file. The file must follow a Json format.
--languageExecutable: the full path to the language executable file (e.g. dotnet.exe, python.exe, node.exe).
--workerExecutable: the full path to the executable file of your language worker or Functions App.
--workerDirectory: the full path to the directory of your language worker or Functions App.
```

## How to Run
* Use the `dotnet build` command
```
dotnet build

cd .\src\WorkerHarness.Console\bin\Debug\net6.0\

.\WorkerHarness.Console.exe --scenarioFile="full\path\to\a\scenario\file" --languageExecutable="full\path\to\language\executable" --workerExecutable="full\path\to\worker\executable" --workerDirectory="full\path\to\worker\directory"
```
* Use the `dotnet run` command
```
dotnet run --project="src\WorkerHarness.Console" --scenarioFile="full\path\to\a\scenario\file" --languageExecutable="full\path\to\language\executable" --workerExecutable="full\path\to\worker\executable" --workerDirectory="full\path\to\worker\directory"
```

## Example
A .NET language developer want to test a .NET language worker. The language developer builds a Functions App using the .NET language worker. 
1. Create a scenario file. See [Scenario](#scenario) section on how to create a scenario json file.
2. Identify the language executable file to be *dotnet.exe*. 
3. Identify the worker executable file to be a Functions App *.dll* file. 
4. Identify the worker directory to be the 

# Scenario

