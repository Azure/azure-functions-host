# steps to transform WebHost to FabricService/Host

1. copy WebJobs.Script.sln to WebJobs.Script.SF-open-elevated.sln
2. add Service Fabric Web API project to solution WebJobs.Script.SF-open-elevated.sln
3. replicate folder structure and link files from WebHost, except for...
4. ...resource/property related files that are needed locally in FabricService project. Copy and adjust:
    - Resources.resx
    - Home.html
    - Controllers\HomeController.cs
    - Controllers\KeysController.cs
    - Controllers\SwaggerController.cs
    - Security\SecretManager.cs
    - Security\Serialization\ScriptSecretSerializerV0.cs
    - Security\Serialization\ScriptSecretSerializerV1.cs    
5. transform WebHosts WebApiConfig.cs into Startup.cs

# get FabricService/Host running

1. open solution elevated
2. create AppInsights instance and/or get Instrumentation key
3. untrack eventFlowConfig: git update-index --assume-unchanged .\src\WebJobs.Script.FabricService\PackageRoot\Config\eventFlowConfig.json
4. set Instrumentation key in .\src\WebJobs.Script.FabricService\PackageRoot\Config\eventFlowConfig.json, remove StdOutput 