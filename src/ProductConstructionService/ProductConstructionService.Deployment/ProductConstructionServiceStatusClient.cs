// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Azure.Core;
using Maestro.Common.AppCredentials;

namespace ProductConstructionService.Deployment;

public class ProductConstructionServiceStatusClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _productConstructionServiceStatusEndpoint;

    private string StatusEndpoint => _productConstructionServiceStatusEndpoint;
    private string StopEndpoint => $"{StatusEndpoint}/stop";
    private string StartEndpoint => $"{StatusEndpoint}/start";

    private const int WaitTimeDelaySeconds = 20;

    public ProductConstructionServiceStatusClient(
        string entraAppId,
        bool disableInteractiveAuth,
        string productConstructionServiceStatusEndpoint,
        string? accessToken = null)
    {
        var credential = AppCredentialResolver.CreateCredential(
            new AppCredentialResolverOptions(entraAppId)
            {
                DisableInteractiveAuth = disableInteractiveAuth,
                Token = accessToken,
                ManagedIdentityId = null,
                UserScope = "Maestro.User",
            });
        var token = credential.GetToken(new TokenRequestContext(), default);
        _httpClient = new(new HttpClientHandler()
        {
            CheckCertificateRevocationList = true
        });
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token.Token}");

        _productConstructionServiceStatusEndpoint = productConstructionServiceStatusEndpoint;
    }

    public async Task StopProcessingNewJobs()
    {
        try
        {
            Console.WriteLine("Stopping the service from processing new jobs");
            var stopResponse = await _httpClient.PutAsync(StopEndpoint, null);

            stopResponse.EnsureSuccessStatusCode();

            string status;
            do
            {
                var statusResponse = await _httpClient.GetAsync(StatusEndpoint);
                statusResponse.EnsureSuccessStatusCode();

                status = await statusResponse.Content.ReadAsStringAsync();
                Console.WriteLine($"Current status: {status}");
            }
            while (status != "Stopped" && await Utility.Sleep(WaitTimeDelaySeconds));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex}. Deploying the new revision without stopping the service");
        }
    }

    public async Task StartService()
    {
        var response = await _httpClient.PutAsync(StartEndpoint, null);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _httpClient.Dispose();
}
