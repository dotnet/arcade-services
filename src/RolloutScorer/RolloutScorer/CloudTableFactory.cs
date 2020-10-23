using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;

namespace RolloutScorer
{
    public interface ICloudTableFactory
    {
        CloudTable CreateScoreCardTable(string storageAccountKey);
        CloudTable CreateDeploymentTable(string uri);
    }

    public class CloudTableFactory : ICloudTableFactory
    {
        public CloudTable CreateDeploymentTable(string uri)
        {
            return new CloudTable(new Uri(uri));
        }

        public CloudTable CreateScoreCardTable(string storageAccountKey)
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(
                connectionString: $"DefaultEndpointsProtocol=https;AccountName={ScorecardsStorageAccount.Name};AccountKey={storageAccountKey};EndpointSuffix=core.windows.net");
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            return tableClient.GetTableReference(ScorecardsStorageAccount.ScorecardsTableName);
        }
    }
}
