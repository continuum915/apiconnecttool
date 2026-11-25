using ApiConnectTool.Models;
using ApiConnectTool.Services;

namespace ApiConnectTool.Tests.ViewModels;

/// <summary>History store kept in memory, newest first, for view model tests.</summary>
internal sealed class InMemoryHistoryStore : IHistoryStore
{
    public List<HistoryEntry> Entries { get; } = new();

    public void Add(HistoryEntry entry) => Entries.Insert(0, entry);

    public IReadOnlyList<HistoryEntry> GetAll() => Entries;

    public void Clear() => Entries.Clear();
}
