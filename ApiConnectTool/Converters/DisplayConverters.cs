using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ApiConnectTool.Converters;

/// <summary>Formatting converters shared by views.</summary>
public static class DisplayConverters
{
    /// <summary>A UTC timestamp shown in local time, short date and time.</summary>
    public static readonly IValueConverter LocalDateTime =
        new FuncValueConverter<DateTimeOffset, string>(value => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

    /// <summary>A non-negative weight as a star grid length, for proportional bars.</summary>
    public static readonly IValueConverter Star =
        new FuncValueConverter<double, Avalonia.Controls.GridLength>(value => new Avalonia.Controls.GridLength(Math.Max(0, value), Avalonia.Controls.GridUnitType.Star));

    /// <summary>The icon for a run result: a check, a cross or a warning sign.</summary>
    public static readonly IValueConverter RunOutcomeIcon =
        new FuncValueConverter<ApiConnectTool.Models.RunEntryOutcome, string>(outcome => outcome switch
        {
            ApiConnectTool.Models.RunEntryOutcome.Passed => "Check",
            ApiConnectTool.Models.RunEntryOutcome.Failed => "Cross",
            _ => "Warning"
        });

    /// <summary>A UTC timestamp shown as local time of day.</summary>
    public static readonly IValueConverter LocalTime =
        new FuncValueConverter<DateTimeOffset, string>(value => value.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
}
