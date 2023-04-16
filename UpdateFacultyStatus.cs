using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Azure.WebJobs;

namespace UpdateFacultyStatus
{
    public static class UpdateFacultyStatus
    {
        public static CosmosClient cosmosClient;
        public static IConfiguration config;
        private const int MAX_IDLE_TIME_SECONDS = 60;

        [Function("UpdateFacultyStatus")]
        public static async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post")] HttpRequestData req,
            FunctionContext context)
        {
            var logger = context.GetLogger("UpdateFacultyStatus");

            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("localsettings.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables()
                    .Build();

                var cs = config["connectionString"];
                // Initialize CosmosClient if not already initialized


                cosmosClient = new CosmosClient(cs);


                var query = req.Url.Query;
                var from = System.Web.HttpUtility.ParseQueryString(query)["deviceAddress"];

                // Get the device address from the HTTP request
                if (!string.IsNullOrEmpty(from))
                {
                    // Update the status of the faculty in the database
                    var databaseName = "FacultyAvailabilityDB";
                    var containerName = "FacultyAvailabilityCollection";
                    var container = cosmosClient.GetContainer(databaseName, containerName);
                    var queryDefinition = new QueryDefinition("SELECT * FROM c WHERE c.deviceAddress = @deviceAddress")
                        .WithParameter("@deviceAddress", from);
                    var iterator = container.GetItemQueryIterator<JObject>(queryDefinition);
                    var results = new List<JObject>();
                    while (iterator.HasMoreResults)
                    {
                        var response = await iterator.ReadNextAsync();
                        results.AddRange(response);
                    }
                    if (results.Count > 0)
                    {
                        var faculty = results[0];
                        faculty["status"] = "available";
                        faculty["lastSeenTimestamp"] = DateTime.UtcNow.ToString("o"); // Update last seen timestamp to current UTC time
                        await container.ReplaceItemAsync(faculty, faculty["id"].ToString());
                    }

                    // Return a response to the HTTP request
                    var responseData = req.CreateResponse(HttpStatusCode.OK);
                    responseData.WriteString("Status updated successfully");
                    return responseData;
                }
                else
                {
                    // Return an error response if 'deviceAddress' query parameter is missing
                    var responseData = req.CreateResponse(HttpStatusCode.BadRequest);
                    responseData.WriteString("Missing 'deviceAddress' query parameter");
                    return responseData;
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error updating faculty status: {ex.Message}");
                var responseData = req.CreateResponse(HttpStatusCode.InternalServerError);
                responseData.WriteString("Error updating faculty status");
                return responseData;
            }
        }
    }
}
