using System;
using System.Threading;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface ICollectionRunner
{
    Task<CollectionRunReport> RunAsync(RequestCollection collection, CollectionRunConfiguration settings,
        IProgress<CollectionRunProgress>? progress = null, CancellationToken cancellationToken = default);
}
