using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

/// <summary>
/// Gates sends that use environments marked IsProduction, per the workspace's configured
/// ProductionWarningMode. NeverWarn always proceeds without prompting; WarnOncePerSession prompts
/// until the user has confirmed each production environment involved once in this instance's
/// lifetime (declining does not count as a confirmation, so the next send prompts again);
/// AlwaysWarn prompts on every send. A send that uses two production environments prompts once.
/// </summary>
public sealed class ProductionSendGuard(Func<ProductionWarningMode> modeProvider,
    Func<IReadOnlyList<ProductionTarget>, Task<bool>> promptAsync) : IProductionSendGuard
{
    private readonly HashSet<Guid> _confirmedThisSession = new();

    public async Task<bool> ConfirmSendAsync(IReadOnlyList<ProductionTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var production = targets.Where(target => target.Environment.IsProduction).ToArray();
        if (production.Length == 0) return true;
        var mode = modeProvider();
        if (mode == ProductionWarningMode.NeverWarn) return true;
        if (mode == ProductionWarningMode.WarnOncePerSession &&
            production.All(target => _confirmedThisSession.Contains(target.Environment.Id)))
            return true;

        var confirmed = await promptAsync(production).ConfigureAwait(false);
        if (confirmed)
            foreach (var target in production) _confirmedThisSession.Add(target.Environment.Id);
        return confirmed;
    }
}
