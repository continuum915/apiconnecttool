using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public interface IHistoryStore
{
    string? PersistenceWarning => null;

    /// <summary>Raised, possibly on a background thread, when <see cref="PersistenceWarning"/> changes.</summary>
    event EventHandler? PersistenceWarningChanged { add { } remove { } }

    /// <summary>Records a completed send in memory and queues it for saving; never waits on disk.
    /// Evicts the oldest entry first once the bound is exceeded.</summary>
    void Add(HistoryEntry entry);

    /// <summary>Removes every entry and saves the empty history with the same atomic write as
    /// <see cref="Add"/>.</summary>
    void Clear() { }

    /// <summary>All recorded entries, newest first.</summary>
    IReadOnlyList<HistoryEntry> GetAll();

    /// <summary>Completes once every queued entry is on disk, or once the attempt has failed
    /// (reported through <see cref="PersistenceWarning"/>). Never throws for storage failures.</summary>
    Task FlushAsync() => Task.CompletedTask;
}
