using System;
using System.Security.Claims;
using System.Threading.Tasks;
using DotNet.Status.Web;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.DotNet.Web.Authentication.GitHub;

namespace DotNet.Status.Web.Pages
{
    public class TokenModel : PageModel
    {
        private readonly ITokenStore _tokens;
        private readonly ITokenRevocationProvider _revocation;
        private readonly GitHubUserTokenHandler _handler;
        private readonly GitHubClaimResolver _resolver;

        public TokenModel(
            ITokenStore tokens,
            ITokenRevocationProvider revocation,
            GitHubUserTokenHandler handler,
            GitHubClaimResolver resolver)
        {
            _tokens = tokens;
            _revocation = revocation;
            _handler = handler;
            _resolver = resolver;
        }

        public string TokenValue { get; set; }

        [BindProperty]
        public string Description { get; set; }

        [BindProperty]
        public long RevokeTokenId { get; set; }

        public void OnGet()
        {
        }

        public async Task OnPostNewToken()
        {
            StoredTokenData token = await _tokens.IssueTokenAsync(
                UserId,
                DateTimeOffset.UtcNow.AddYears(1),
                Description);

            TokenValue = _handler.EncodeToken(User, token);
        }

        private long UserId => long.Parse(User.FindFirst(ClaimTypes.NameIdentifier).Value);

        public async Task<IActionResult> OnPostRevoke()
        {
            await _revocation.RevokeTokenAsync(UserId, RevokeTokenId);
            return Redirect(Request.Path);
        }
    }
}
