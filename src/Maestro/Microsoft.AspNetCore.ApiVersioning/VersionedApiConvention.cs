// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.ApiVersioning
{
    [PublicAPI]
    public class VersionedApiConvention : IApplicationModelConvention
    {
        public VersionedApiConvention(
            ILogger<VersionedApiConvention> logger,
            VersionedControllerProvider controllerProvider,
            IOptions<ApiVersioningOptions> optionsAccessor)
        {
            Logger = logger;
            ControllerProvider = controllerProvider;
            Options = optionsAccessor.Value;
        }

        private  ILogger<VersionedApiConvention> Logger { get; }
        private VersionedControllerProvider ControllerProvider { get; }
        private ApiVersioningOptions Options { get; }

        public void Apply(ApplicationModel application)
        {
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, TypeInfo>> versions = ControllerProvider.Versions;
            List<TypeInfo> controllerTypesToRemove = versions.SelectMany(v => v.Value.Values).ToList();

            var versionedModels = new Dictionary<string, ControllerModel>();
            foreach (ControllerModel model in application.Controllers)
            {
                if (controllerTypesToRemove.Any(t => Equals(model.ControllerType, t)))
                {
                    string typeName = model.ControllerType.AssemblyQualifiedName;
                    versionedModels.Add(typeName, model);
                }
            }

            foreach (ControllerModel model in versionedModels.Values)
            {
                Logger.LogTrace("Removing: {controllerType}", model.ControllerType.AssemblyQualifiedName);
                application.Controllers.Remove(model);
            }

            foreach (KeyValuePair<string, IReadOnlyDictionary<string, TypeInfo>> version in versions)
            {
                AddVersion(application, version.Key, version.Value, versionedModels);
            }
        }

        private void AddVersion(ApplicationModel application, string version, IReadOnlyDictionary<string, TypeInfo> controllers, Dictionary<string, ControllerModel> versionedModels)
        {
            foreach (KeyValuePair<string, TypeInfo> controller in controllers)
            {
                string controllerTypeName = controller.Value.AssemblyQualifiedName;
                var controllerModel = new ControllerModel(versionedModels[controllerTypeName])
                {
                    ControllerName = $"{controller.Key}/{version}",
                    RouteValues =
                    {
                        ["version"] = version,
                    },
                };
                if (controllerModel.Selectors.Count > 1)
                {
                    throw new InvalidOperationException("Versioned Controllers cannot have more than one route.");
                }

                RemoveApiRemovedActions(controllerModel);

                SelectorModel selector = controllerModel.Selectors.Count == 1
                    ? controllerModel.Selectors[0]
                    : GetDefaultControllerSelector(controller);
                if (selector.AttributeRouteModel.IsAbsoluteTemplate)
                {
                    throw new InvalidOperationException(
                        "versioned api controllers are not allowed to have absolute routes.");
                }

                controllerModel.Selectors.Clear();
                Options.VersioningScheme.Apply(selector, version);
                controllerModel.Selectors.Add(selector);

                application.Controllers.Add(controllerModel);
                controllerModel.Application = application;
            }
        }

        public void RemoveApiRemovedActions(ControllerModel controller)
        {
            var actionsToRemove = new List<ActionModel>();
            foreach (var action in controller.Actions)
            {
                if (action.ActionMethod.IsDefined(typeof(ApiRemovedAttribute)))
                {
                    actionsToRemove.Add(action);
                }
            }

            foreach (var action in actionsToRemove)
            {
                controller.Actions.Remove(action);
            }
        }

        private SelectorModel GetDefaultControllerSelector(KeyValuePair<string, TypeInfo> controller)
        {
            return new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(controller.Key))
            };
        }
    }
}
