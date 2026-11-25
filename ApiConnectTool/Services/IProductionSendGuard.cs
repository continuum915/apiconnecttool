using System.Collections.Generic;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IProductionSendGuard
{
    /// <summary>Returns true if the send should proceed. Prompts at most once for the whole set of
    /// production environments the send uses, depending on the configured ProductionWarningMode and
    /// which of those environments were already confirmed this session.</summary>
    Task<bool> ConfirmSendAsync(IReadOnlyList<ProductionTarget> targets);
}
