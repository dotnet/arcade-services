// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DotNet.Status.Web.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;

namespace DotNet.Status.Web
{
    public class AzureTableTokenStore : ITokenStore, ITokenRevocationProvider
    {
        private readonly RNGCryptoServiceProvider _random = new RNGCryptoServiceProvider();
        private readonly IHostEnvironment _env;
        private readonly IOptionsMonitor<AzureTableTokenStoreOptions> _options;
        private readonly ILogger<AzureTableTokenStore> _logger;

        public AzureTableTokenStore(
            IHostEnvironment env,
            IOptionsMonitor<AzureTableTokenStoreOptions> options,
            ILogger<AzureTableTokenStore> logger)
        {
            _env = env;
            _options = options;
            _logger = logger;
        }

        private async Task<CloudTable> GetCloudTable()
        {
            CloudTable table;
            if (_env.IsDevelopment())
            {
                table = CloudStorageAccount.DevelopmentStorageAccount.CreateCloudTableClient().GetTableReference("githubtokens");
                await table.CreateIfNotExistsAsync();
            }
            else
            {
                AzureTableTokenStoreOptions options = _options.CurrentValue;
                table = new CloudTable(new Uri(options.TableUri, UriKind.Absolute), new StorageCredentials(options.TableSasToken));
            }
            return table;
        }

        public async Task<bool> IsTokenRevokedAsync(long userId, long tokenId)
        {
            var token = await GetTokenAsync(userId, tokenId);
            return token.RevocationStatus != RevocationStatus.Active;
        }

        private Task UpdateAsync(long userId, long tokenId, Action<TokenEntity> update)
        {
            return UpdateAsync(userId.ToString(), tokenId.ToString(), update);
        }

        private async Task UpdateAsync<T>(string partitionKey, string rowKey, Action<T> update) where T : TableEntity
        {
            CloudTable table = await GetCloudTable();
            while (true)
            {
                TableResult fetchResult = await table.ExecuteAsync(TableOperation.Retrieve<T>(partitionKey, rowKey));
                if (fetchResult.HttpStatusCode != 200)
                    throw new KeyNotFoundException();

                var entity = (T) fetchResult.Result;
                update(entity);

                try
                {
                    await table.ExecuteAsync(TableOperation.Replace(entity));
                    return;
                }
                catch (StorageException e) when (e.RequestInformation.HttpStatusCode == (int) HttpStatusCode.PreconditionFailed)
                {
                    _logger.LogInformation("Concurrent update failed, re-fetching and updating...");
                }
            }
        }

        public async Task RevokeTokenAsync(long userId, long tokenId)
        {
            try
            {
                await UpdateAsync(userId, tokenId, t => t.RevocationStatus = RevocationStatus.Revoked);
            }
            catch (KeyNotFoundException)
            {
                // it doesn't exist, we'll call that revoked
            }
        }

        public async Task<StoredTokenData> IssueTokenAsync(long userId, DateTimeOffset expiration, string description)
        {
            CloudTable table = await GetCloudTable();

            long MakeTokenId()
            {
                Span<byte> bits = stackalloc byte[sizeof(long)];
                _random.GetBytes(bits);
                return BitConverter.ToInt64(bits);
            }

            while (true)
            {
                try
                {
                    var entity = new TokenEntity(
                        userId,
                        MakeTokenId(),
                        DateTimeOffset.UtcNow,
                        expiration,
                        description,
                        RevocationStatus.Active
                    );

                    await table.ExecuteAsync(TableOperation.Insert(entity));

                    return Return(entity);
                }
                catch (StorageException e) when (e.RequestInformation.HttpStatusCode == (int) HttpStatusCode.Conflict)
                {
                    _logger.LogInformation("Duplicate token insertion attempted, generating new ID and retrying...");
                }
            }
        }

        public async Task<StoredTokenData> GetTokenAsync(long userId, long tokenId)
        {
            CloudTable table = await GetCloudTable();
            TableResult result = await table.ExecuteAsync(TableOperation.Retrieve<TokenEntity>(userId.ToString(), tokenId.ToString()));
            if (result.HttpStatusCode != (int) HttpStatusCode.OK)
            {
                return null;
            }

            return Return((TokenEntity) result.Result);
        }

        public async Task<IEnumerable<StoredTokenData>> GetTokensForUserAsync(
            long userId,
            CancellationToken cancellationToken)
        {
            CloudTable table = await GetCloudTable();
            TableContinuationToken continuationToken = null;
            TableQuery<TokenEntity> query = new TableQuery<TokenEntity>().Where(
                TableQuery.GenerateFilterCondition(
                    nameof(TokenEntity.PartitionKey),
                    QueryComparisons.Equal,
                    userId.ToString()));

            List<StoredTokenData> tokens = new List<StoredTokenData>();
            do
            {
                TableQuerySegment<TokenEntity> segment = await table.ExecuteQuerySegmentedAsync(query, continuationToken, requestOptions: null, operationContext: null, cancellationToken);
                continuationToken = segment.ContinuationToken;
                tokens.AddRange(segment.Results.Select(Return));
            } while (continuationToken != null);

            return tokens;
        }

        private StoredTokenData Return(TokenEntity entity)
        {
            return new StoredTokenData(entity.UserId,
                entity.TokenId,
                entity.Issued,
                entity.Expiration,
                entity.Description,
                entity.RevocationStatus);
        }

        private class TokenEntity : TableEntity
        {
            public TokenEntity() : base()
            {
            }

            public TokenEntity(long userId, long tokenId) : base(userId.ToString(), tokenId.ToString())
            {
            }

            public TokenEntity(
                long userId,
                long tokenId,
                DateTimeOffset issued,
                DateTimeOffset expiration,
                string description,
                RevocationStatus revocationStatus) : this(userId, tokenId)
            {
                Issued = issued;
                Expiration = expiration;
                Description = description;
                RevocationStatus = revocationStatus;
            }

            [IgnoreProperty]
            public long UserId
            {
                get => long.Parse(PartitionKey);
                set => PartitionKey = value.ToString();
            }

            [IgnoreProperty]
            public long TokenId
            {
                get => long.Parse(RowKey);
                set => PartitionKey = value.ToString();
            }
            public DateTimeOffset Issued { get; set; }
            public DateTimeOffset Expiration { get; set; }
            public string Description { get; set; }

            [IgnoreProperty]
            public RevocationStatus RevocationStatus
            {
                get => Enum.Parse<RevocationStatus>(RevocationString);
                set => RevocationString = value.ToString();
            }

            public string RevocationString { get; set; }
        }
    }

    public static class AzureTableTokenStoreExtension
    {
        public static IServiceCollection AddAzureTableTokenStore(this IServiceCollection services, Action<AzureTableTokenStoreOptions> configure)
        {
            services.Configure(configure);
            services.AddSingleton<AzureTableTokenStore>();
            services.AddSingleton<ITokenRevocationProvider>(s => s.GetRequiredService<AzureTableTokenStore>());
            services.AddSingleton<ITokenStore>(s => s.GetRequiredService<AzureTableTokenStore>());
            return services;
        }
    }
}
