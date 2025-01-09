// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Microsoft.DotNet.Darc.Operations;

internal class AddChannelOperation : Operation
{
    private readonly AddChannelCommandLineOptions _options;
    private readonly ILogger<AddChannelOperation> _logger;
    private readonly IBarApiClient _barClient;

    public AddChannelOperation(
        AddChannelCommandLineOptions options,
        IBarApiClient barClient,
        ILogger<AddChannelOperation> logger)
    {
        _options = options;
        _barClient = barClient;
        _logger = logger;
    }

    /// <summary>
    /// Adds a new channel with the specified name.
    /// </summary>
    /// <returns>Process exit code.</returns>
    public override async Task<int> ExecuteAsync()
    {
        try
        {
            // If the user tried to mark as internal, indicate that this is currently
            // unsupported.
            if (_options.Internal)
            {
                _logger.LogError("Cannot currently mark channels as internal.");
                return Constants.ErrorCode;
            }

            Channel newChannelInfo = await _barClient.CreateChannelAsync(_options.Name, _options.Classification);
            switch (_options.OutputFormat)
            {
                case DarcOutputType.json:
                    Console.WriteLine(JsonConvert.SerializeObject(
                        new
                        {
                            id = newChannelInfo.Id,
                            name = newChannelInfo.Name,
                            classification = newChannelInfo.Classification
                        },
                        Formatting.Indented));
                    break;
                case DarcOutputType.text:
                    Console.WriteLine($"Successfully created new channel with name '{_options.Name}' and id {newChannelInfo.Id}.");
                    break;
                default:
                    throw new NotImplementedException($"Output type {_options.OutputFormat} not supported by add-channel");
            }

            return Constants.SuccessCode;
        }
        catch (AuthenticationException e)
        {
            Console.WriteLine(e.Message);
            return Constants.ErrorCode;
        }
        catch (RestApiException e) when (e.Response.Status == (int) HttpStatusCode.Conflict)
        {
            _logger.LogError($"An existing channel with name '{_options.Name}' already exists");
            return Constants.ErrorCode;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error: Failed to create new channel.");
            return Constants.ErrorCode;
        }
    }
}
