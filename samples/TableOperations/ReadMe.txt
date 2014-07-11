Windows Azure WebJobs SDK Table Sample
-----------------------------------

This sample demonstrates table bindings and operations with Windows Azure WebJobs SDK. The two 
functions in this example split strings into words (CountAndSplitInWords) and in characters 
(CharFrequency) and computes their frequencies. The results are stored in Azure Storage Tables.

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
