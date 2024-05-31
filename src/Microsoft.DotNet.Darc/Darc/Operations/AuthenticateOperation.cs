// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Darc.Helpers;
using Microsoft.DotNet.Darc.Models;
using Microsoft.DotNet.Darc.Options;
using System.Threading.Tasks;

namespace Microsoft.DotNet.Darc.Operations;

internal class AuthenticateOperation : Operation
{
    private readonly AuthenticateCommandLineOptions _options;
    public AuthenticateOperation(AuthenticateCommandLineOptions options)
        : base(options)
    {
        _options = options;
    }

    /// <summary>
    /// Implements the 'authenticate' verb
    /// </summary>
    /// <param name="options"></param>
    public override Task<int> ExecuteAsync()
    {
        // If clear was passed, then clear the options (no popup)
        if (_options.Clear)
        {
            var defaultSettings = new LocalSettings();
            defaultSettings.SaveSettingsFile(Logger);
            return Task.FromResult(Constants.SuccessCode);
        }
        else
        {
            var initEditorPopUp = new AuthenticateEditorPopUp("authenticate-settings/darc-authenticate", Logger);

            var uxManager = new UxManager(_options.GitLocation, Logger);
            return Task.FromResult(uxManager.PopUp(initEditorPopUp));
        }
    }
}
