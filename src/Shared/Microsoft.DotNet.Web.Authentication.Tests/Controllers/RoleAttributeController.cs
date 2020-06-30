using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.DotNet.Web.Authentication.Tests.Controllers
{
    [Route("test-auth/role")]
    [Authorize(Roles = "ControllerRole")]
    public class RoleAttributeController : ControllerBase
    {
        [Route("no")]
        public IActionResult NoAttribute()
        {
            return Ok("Role:No:Value");
        }

        [AllowAnonymous]
        [Route("anonymous")]
        public IActionResult AnonymousAttribute()
        {
            return Ok("Role:Anonymous:Value");
        }

        [Authorize]
        [Route("any")]
        public IActionResult Any()
        {
            return Ok("Role:Any:Value");
        }

        [Authorize(Roles = "ActionRole")]
        [Route("role")]
        public IActionResult RoleAttribute()
        {
            return Ok("Role:Role:Value");
        }
    }
}
