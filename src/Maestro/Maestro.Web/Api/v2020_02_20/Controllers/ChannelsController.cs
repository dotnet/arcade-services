// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Maestro.Data;
using Maestro.Data.Models;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.DotNet.DarcLib;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Build = Maestro.Data.Models.Build;
using Channel = Maestro.Web.Api.v2020_02_20.Models.Channel;
using FlowGraph = Maestro.Web.Api.v2018_07_16.Models.FlowGraph;

namespace Maestro.Web.Api.v2020_02_20.Controllers
{
    /// <summary>
    ///   Exposes methods to Create/Read/Edit/Delete <see cref="Channel"/>s.
    /// </summary>
    [Route("channels")]
    [ApiVersion("2020-02-20")]
    public class ChannelsController : v2018_07_16.Controllers.ChannelsController
    {
        private readonly BuildAssetRegistryContext _context;
        private readonly IRemoteFactory _remoteFactory;

        public ChannelsController(BuildAssetRegistryContext context,
                                  IRemoteFactory factory,
                                  ILogger<ChannelsController> logger)
           : base(context, factory, logger)
        {
            _context = context;
            _remoteFactory = factory;
        }

        [ApiRemoved]
        public override Task<IActionResult> AddPipelineToChannel(int channelId, int pipelineId)
        {
            throw new NotImplementedException();
        }

        [ApiRemoved]
        public override Task<IActionResult> DeletePipelineFromChannel(int channelId, int pipelineId)
        {
            throw new NotImplementedException();
        }
    }
}
