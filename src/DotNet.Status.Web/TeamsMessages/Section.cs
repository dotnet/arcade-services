using System;

namespace DotNet.Status.Web.TeamsMessages
{
    public class Section
    {
        public Uri ActivityImage { get; set; }

        public string ActivityTitle { get; set; } = string.Empty;

        public string ActivitySubtitle { get; set; } = string.Empty;

        public string ActivityText { get; set; } = string.Empty;
    }
}