dotnet publish src/WebJobs.Script.WebHost/WebJobs.Script.WebHost.csproj -c Release --runtime debian.10-x64 --output $PWD/azure-functions-host
docker build -t customfunctionsimageforlegion .
docker tag customfunctionsimageforlegion yogeshster/customfunctionsimage:1.1.0
docker push yogeshster/customfunctionsimage:1.1.0