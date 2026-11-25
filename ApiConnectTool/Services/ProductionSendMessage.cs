using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ApiConnectTool.Models;
using ApiConnectTool.Services.Localization;

namespace ApiConnectTool.Services;

/// <summary>The production send warning text: each production environment, and the collection
/// that owns it for a collection environment.</summary>
public static class ProductionSendMessage
{
    public static string Format(IReadOnlyList<ProductionTarget> targets) => string.Join(Environment.NewLine,
        [AppLocalization.Get(targets.Count == 1 ? "Str.ProductionSend.IntroOne" : "Str.ProductionSend.IntroMany"),
         .. targets.Select(Describe), AppLocalization.Get("Str.ProductionSend.Question")]);

    public static string Describe(ProductionTarget target) => target.CollectionName is null
        ? string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.ProductionSend.Global"), target.Environment.Name)
        : string.Format(CultureInfo.CurrentCulture, AppLocalization.Get("Str.ProductionSend.Collection"), target.Environment.Name, target.CollectionName);
}
