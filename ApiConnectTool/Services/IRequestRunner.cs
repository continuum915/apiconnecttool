using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IRequestRunner
{
    Task<RequestRunResult> RunAsync(RequestItem request, RequestExecutionOptions? options = null,
        CancellationToken cancellationToken = default, int scriptTimeoutMilliseconds = 2000);
}
