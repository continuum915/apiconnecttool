using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.ViewModels;

/// <summary>One entry of a collection environment picker: an environment, or "No environment"
/// when <see cref="Environment"/> is null. <see cref="Name"/> is the display text.</summary>
public sealed record EnvironmentChoice(RequestEnvironment? Environment, string Name)
{
    public bool IsProduction => Environment?.IsProduction == true;

    /// <summary>The environment's name, marked when it is a production environment.</summary>
    public static string Label(RequestEnvironment environment) => environment.IsProduction
        ? string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.CollectionEnvironment.ProductionLabel"), environment.Name)
        : environment.Name;

    /// <summary>"No environment" followed by <paramref name="collection"/>'s environments in order.
    /// Names are plain; views mark production with the PROD tag from <see cref="IsProduction"/>.</summary>
    public static IReadOnlyList<EnvironmentChoice> ForCollection(RequestCollection collection) =>
        [new(null, AppLocalization.Get("Str.Runner.NoCollectionEnvironment")),
            .. collection.Environments.Select(environment => new EnvironmentChoice(environment, environment.Name))];
}
