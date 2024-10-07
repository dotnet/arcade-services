// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.BarViz.Code.Helpers;

public static class DateFormatter
{
    public static string FormatDate(DateTimeOffset date)
    {
        var now = DateTimeOffset.Now;
        var time = date.ToString("HH:mm");
        var dateFormat = "yyyy-MM-dd";

        if (date.Date == now.Date)
        {
            return $"Today at {time}";
        }
        else if (date.Date == now.AddDays(-1).Date)
        {
            return $"Yesterday at {time}";
        }
        else if (date.Date >= now.AddDays(-7).Date)
        {
            return $"Last {date.DayOfWeek} at {time}";
        }
        else
        {
            return date.ToString(dateFormat);
        }
    }
}
