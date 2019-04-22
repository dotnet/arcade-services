using Microsoft.DotNet.DarcLib;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TestPipelineRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            Teste();


            Console.ReadKey();
        }

        public static async void Teste()
        {
            for (int i = 0; i < 1000; i++)
            {
                Console.WriteLine($"Iteration {i}: ");
                await RunAssociatedReleasePipelinesAsync(127402);
                Console.WriteLine("\n");

                //await Task.Delay(2000);
            }
        }

        public static async Task RunAssociatedReleasePipelinesAsync(int buildId)
        {
            string organization = "dnceng";
            string project = "internal";
            int pipelineId = 52;

            AzureDevOpsClient azdoClient = GetAzureDevOpsClientForAccount();

            var azdoBuild = azdoClient.GetBuildAsync(organization, project, buildId).GetAwaiter().GetResult();          
            
            try
            {
                Console.WriteLine($"Going to create a release using pipeline {organization}/{project}/{pipelineId}");

                AzureDevOpsReleaseDefinition pipeDef = await azdoClient.GetReleaseDefinitionAsync(organization, project, pipelineId);
                pipeDef = await azdoClient.RemoveAllArtifactSourcesAsync(organization, project, pipeDef);

                pipeDef = await azdoClient.AddArtifactSourceAsync(organization, project, pipeDef, azdoBuild);

                int releaseId = azdoClient.StartNewReleaseAsync(organization, project, pipeDef, buildId).GetAwaiter().GetResult();

                Console.WriteLine($"Done using pipeline {organization}/{project}/{pipelineId}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Some problem happened while starting publishing pipeline");
                Console.WriteLine(e);
            }
        }

        private static AzureDevOpsClient GetAzureDevOpsClientForAccount()
        {
            string accessToken = "2hxgrefsywuytisxnx5oc7k2n6awu4tssgpgm7ay4scczpmsbafa";
            return new AzureDevOpsClient(accessToken, null, null);
        }
    }
}
