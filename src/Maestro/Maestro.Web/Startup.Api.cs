// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Swashbuckle.AspNetCore.Swagger;

namespace Maestro.Web
{
    public partial class Startup
    {
        private void ConfigureApiServices(IServiceCollection services)
        {
            services.AddApiVersioning(options => options.VersionByQuery("api-version"));
            services.AddSwaggerApiVersioning();
            services.Configure<MvcJsonOptions>(
                options =>
                {
                    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    options.SerializerSettings.Converters.Add(new StringEnumConverter {CamelCaseText = true});
                    options.SerializerSettings.Converters.Add(
                        new IsoDateTimeConverter
                        {
                            DateTimeFormat = "yyyy-MM-ddTHH:mm:ssZ",
                            DateTimeStyles = DateTimeStyles.AdjustToUniversal
                        });
                });

            services.AddSwaggerGen(
                options =>
                {
                    // If you get an exception saying 'Identical schemaIds detected for types Maestro.Web.Api.<version>.Models.<something> and Maestro.Web.Api.<different-version>.Models.<something>'
                    // Then you should NEVER add the following like the StackOverflow answers will suggest.
                    // options.CustomSchemaIds(x => x.FullName);

                    // This exception means that you have added something to one of the versions of the api that results in a conflict, because one version of the api cannot have 2 models for the same object
                    // e.g. If you add a new api, or just a new model to an existing version (with new or modified properties), every method in the API that can return this type,
                    // even nested (e.g. you changed Build, and Subscription contains a Build object), must be updated to return the new type.
                    // It could also mean that you forgot to apply [ApiRemoved] to an inherited method that shouldn't be included in the new version

                    options.FilterOperations(
                        (op, ctx) =>
                        {
                            op.Responses["default"] = new Response
                            {
                                Description = "Error",
                                Schema = ctx.SchemaRegistry.GetOrRegister(typeof(ApiError))
                            };
                            op.OperationId = $"{op.Tags.First()}_{op.OperationId}";
                        });

                    options.MapType<TimeSpan>(
                        () => new Schema
                        {
                            Type = "string",
                            Format = "duration"
                        });
                    options.MapType<TimeSpan?>(
                        () => new Schema
                        {
                            Type = "string",
                            Format = "duration"
                        });
                    options.MapType<JToken>(() => new Schema());

                    options.DescribeAllEnumsAsStrings();

                    string xmlPath;
                    if (HostingEnvironment.IsDevelopment())
                    {
                        xmlPath = Path.Combine(HostingEnvironment.ContentRootPath, "bin/Debug/net461");
                    }
                    else
                    {
                        xmlPath = HostingEnvironment.ContentRootPath;
                    }

                    string xmlFile = Path.Combine(xmlPath, "Maestro.Web.xml");
                    if (File.Exists(xmlFile))
                    {
                        options.IncludeXmlComments(xmlFile);
                    }

                    options.AddSecurityDefinition(
                        "Bearer",
                        new ApiKeyScheme
                        {
                            Type = "apiKey",
                            In = "header",
                            Name = "Authorization"
                        });

                    options.FilterDocument(
                        (doc, ctx) =>
                        {
                            doc.Security = new List<IDictionary<string, IEnumerable<string>>>
                            {
                                new Dictionary<string, IEnumerable<string>>
                                {
                                    ["Bearer"] = Enumerable.Empty<string>()
                                }
                            };
                        });
                });
        }
    }
}
