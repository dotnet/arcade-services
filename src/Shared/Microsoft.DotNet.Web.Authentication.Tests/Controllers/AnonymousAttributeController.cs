using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.DotNet.Web.Authentication.Tests.Controllers
{
    [Route("test-auth/anonymous")]
    [AllowAnonymous]
    public class AnonymousAttributeController : ControllerBase
    {
        [Route("no")]
        public IActionResult NoAttribute()
        {
            return Ok("Anonymous:No:Value");
        }

        [AllowAnonymous]
        [Route("anonymous")]
        public IActionResult AnonymousAttribute()
        {
            return Ok("Anonymous:Anonymous:Value");
        }

        [Authorize]
        [Route("any")]
        public IActionResult Any()
        {
            return Ok("Anonymous:Any:Value");
        }

        [Authorize(Roles = "ActionRole")]
        [Route("role")]
        public IActionResult RoleAttribute()
        {
            return Ok("Anonymous:Role:Value");
        }
    }
}
