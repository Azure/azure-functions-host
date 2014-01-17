Windows Azure WebJobs SDK Hello World Sample
-----------------------------------

This sample can be used as a start point for working with Windows Azure WebJobs SDK. It demonstrates
the basic bindings to queue and blob. It also shows the triggering mechanism for queue messages.

In order to run the sample, you need to set the Windows Azure Storage connection strings in the
App.config file or environment variables.
Example (app.config):
  <add name="AzureJobsRuntime" connectionString="DefaultEndpointsProtocol=https;AccountName=NAME;AccountKey=KEY" />
  <add name="AzureJobsData" connectionString="DefaultEndpointsProtocol=https;AccountName=NAME;AccountKey=KEY" />
Example (environment variables):
  SET AzureJobsRuntime=DefaultEndpointsProtocol=https;AccountName=NAME;AccountKey=KEY
  SET AzureJobsData=DefaultEndpointsProtocol=https;AccountName=NAME;AccountKey=KEY

For more information about the WebJobs feature of Windows Azure Web Sites, 
see http://go.microsoft.com/fwlink/?LinkId=390226