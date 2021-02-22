using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace HelixAPI.Pages
{
    public class RoutesModel : PageModel
    {
        private readonly IActionDescriptorCollectionProvider _actionDescriptorCollectionProvider;
        private readonly IHostEnvironment _environment;

        public RoutesModel(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider, IHostEnvironment environment)
        {
            this._actionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
            _environment = environment;
        }

        public List<RouteInfo> Routes { get; set; }

        public IActionResult OnGet()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }
            Routes = _actionDescriptorCollectionProvider.ActionDescriptors.Items
                .Select(x => new RouteInfo
                {
                    Action = x.RouteValues["Action"],
                    Controller = x.RouteValues["Controller"],
                    Page = x.RouteValues["Page"],
                    Name = x.AttributeRouteInfo?.Name,
                    Template = x.AttributeRouteInfo?.Template,
                    Constraint = x.ActionConstraints == null ? "" : JsonConvert.SerializeObject(x.ActionConstraints)
                })
                .OrderBy(r => r.Template)
                .ToList();

            return Page();
        }

        public class RouteInfo
        {
            public string Template { get; set; }
            public string Name { get; set; }
            public string Controller { get; set; }
            public string Action { get; set; }
            public string Constraint { get; set; }
            public string Page { get; set; }
        }
    }
}
