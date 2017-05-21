# steps to transform WebHost to FabricService/Host

1. add Service Fabric Web API project to solution
2. replicate folder structure and link files from WebHost, except for...
3. ...resource/property related files that are needed locally in FabricService project. Copy and adjust:
    - Resources.resx
    - Home.html
    - Controllers\HomeController.cs
    - Controllers\KeysController.cs
    - Controllers\SwaggerController.cs
    - Security\SecretManager.cs
    - Security\Serialization\ScriptSecretSerializerV0.cs
    - Security\Serialization\ScriptSecretSerializerV1.cs    
4. transform WebHosts WebApiConfig.cs into Startup.cs
