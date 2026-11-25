using System;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IRequestExecutionService
{
    Task<RequestExecutionResult> ExecuteAsync(
        RequestItem request,
        RequestExecutionOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<RequestDownloadResult> DownloadAsync(
        RequestItem request,
        string destinationPath,
        RequestExecutionOptions? options = null,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);
}
