using System;
using System.Collections.Generic;
using System.Linq;
using ApiConnectTool.Models;

namespace ApiConnectTool.Services;

public sealed record EffectiveAuth(RequestAuth Auth, string? SourceName);

public static class EffectiveAuthResolver
{
    public static EffectiveAuth Resolve(RequestItem request, RequestCollection? collection)
    {
        if (request.Auth.Type != AuthType.Inherit) return new(request.Auth, null);
        var visited = new HashSet<Guid>();
        var folderId = request.FolderId;
        while (collection is not null && folderId is { } id && visited.Add(id))
        {
            var folder = collection.Folders.FirstOrDefault(item => item.Id == id);
            if (folder is null) break;
            if (folder.Auth.Type != AuthType.Inherit) return new(folder.Auth, folder.Name);
            folderId = folder.ParentFolderId;
        }
        return collection is null ? new(new(), null) : new(collection.Auth, collection.Name);
    }
}
