using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.DotNet.Web.Authentication.Tests.Controllers
{
    [Route("test-auth/no")]
    public class NoAttributeController : ControllerBase
    {
        [Route("no")]
        public IActionResult NoAttribute()
        {
            return Ok("No:No:Value");
        }

        [AllowAnonymous]
        [Route("anonymous")]
        public IActionResult AnonymousAttribute()
        {
            return Ok("No:Anonymous:Value");
        }

        [Authorize]
        [Route("any")]
        public IActionResult Any()
        {
            return Ok("No:Any:Value");
        }

        [Authorize(Roles = "ActionRole")]
        [Route("role")]
        public IActionResult RoleAttribute()
        {
            return Ok("No:Role:Value");
        }
    }
}
