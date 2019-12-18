// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNet.Status.Web.Controllers
{
    [ApiController]
    [Route("api/github-hook")]
    public class GitHubHookController : ControllerBase
    {
        [HttpPost]
        [AllowAnonymous]
        public IActionResult AcceptHook()
        {
            // Ignore them, none are interesting
            return NoContent();
        }
    }
}
