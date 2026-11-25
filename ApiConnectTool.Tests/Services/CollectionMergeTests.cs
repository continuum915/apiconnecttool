using ApiConnectTool.Models;
using ApiConnectTool.Services;
using Xunit;

namespace ApiConnectTool.Tests.Services;

public class CollectionMergeTests
{
    private static (RequestCollection Target, CollectionFolder Admin, RequestItem Purge, RequestItem List, RequestEnvironment Dev) Target()
    {
        var admin = new CollectionFolder { Name = "Admin" };
        var purge = new RequestItem { Name = "Purge", FolderId = admin.Id, Url = "https://old.test/purge", SortOrder = 4 };
        var list = new RequestItem { Name = "List", Url = "https://old.test/list" };
        var dev = new RequestEnvironment { Name = "dev", Variables = { new() { Key = "host", InitialValue = "old.test" } } };
        var target = new RequestCollection
        {
            Name = "Orders", Folders = { admin }, Requests = { purge, list },
            Variables = { new() { Key = "version", InitialValue = "v1" } },
            Environments = { dev }, ActiveEnvironmentId = dev.Id
        };
        return (target, admin, purge, list, dev);
    }

    private static RequestCollection Source()
    {
        var admin = new CollectionFolder { Name = "ADMIN" };
        var nested = new CollectionFolder { Name = "Reports", ParentFolderId = admin.Id };
        return new RequestCollection
        {
            Name = "Incoming", Folders = { admin, nested },
            Requests =
            {
                new() { Name = "purge", FolderId = admin.Id, Url = "https://new.test/purge", Method = HttpMethod.Delete },
                new() { Name = "Daily", FolderId = nested.Id, Url = "https://new.test/daily" },
                new() { Name = "List", FolderId = admin.Id, Url = "https://new.test/admin-list" },
                new() { Name = "LIST", Url = "https://new.test/list" }
            },
            Variables = { new() { Key = "VERSION", InitialValue = "v2" }, new() { Key = "region", InitialValue = "eu" } },
            Environments =
            {
                new RequestEnvironment { Name = "Dev", IsProduction = true, Variables = { new() { Key = "token", InitialValue = "s3cret", IsSecret = true } } },
                new RequestEnvironment { Name = "qa" }
            }
        };
    }

    private static HashSet<Guid> All(RequestCollection source) =>
        source.Folders.Select(item => item.Id).Concat(source.Requests.Select(item => item.Id))
            .Concat(source.Variables.Select(item => item.Id)).Concat(source.Environments.Select(item => item.Id)).ToHashSet();

    [Fact]
    public void Plan_MatchesFoldersByPathAndRequestsByNameInTheSameFolder_IgnoringCase()
    {
        var (target, _, _, _, _) = Target();
        var source = Source();
        var plan = CollectionMerge.Plan(source, target, overwrite: false);

        Assert.Equal(ImportAction.Merge, plan[source.Folders[0].Id]);
        Assert.Equal(ImportAction.New, plan[source.Folders[1].Id]);
        Assert.Equal(ImportAction.Skip, plan[source.Requests[0].Id]);   // purge in Admin
        Assert.Equal(ImportAction.New, plan[source.Requests[1].Id]);    // in a new folder
        Assert.Equal(ImportAction.New, plan[source.Requests[2].Id]);    // List exists only at the root
        Assert.Equal(ImportAction.Skip, plan[source.Requests[3].Id]);   // LIST at the root
        Assert.Equal(ImportAction.Skip, plan[source.Variables[0].Id]);
        Assert.Equal(ImportAction.New, plan[source.Variables[1].Id]);
        Assert.Equal(ImportAction.Skip, plan[source.Environments[0].Id]);
        Assert.Equal(ImportAction.New, plan[source.Environments[1].Id]);

        var overwrite = CollectionMerge.Plan(source, target, overwrite: true);
        Assert.Equal(ImportAction.Overwrite, overwrite[source.Requests[0].Id]);
        Assert.Equal(ImportAction.Overwrite, overwrite[source.Variables[0].Id]);
        Assert.Equal(ImportAction.Overwrite, overwrite[source.Environments[0].Id]);
        Assert.Equal(ImportAction.Merge, overwrite[source.Folders[0].Id]);
    }

    [Fact]
    public void Apply_WithoutOverwrite_AddsOnlyNewItems()
    {
        var (target, admin, purge, list, _) = Target();
        var source = Source();
        var result = CollectionMerge.Apply(source, target, All(source), overwrite: false, importEnvironments: true);

        Assert.Equal("https://old.test/purge", purge.Url);
        Assert.Equal("https://old.test/list", list.Url);
        var reports = Assert.Single(target.Folders, folder => folder.Name == "Reports");
        Assert.Equal(admin.Id, reports.ParentFolderId);
        Assert.Equal(reports.Id, Assert.Single(target.Requests, request => request.Name == "Daily").FolderId);
        Assert.Equal(admin.Id, Assert.Single(target.Requests, request => request.Url == "https://new.test/admin-list").FolderId);
        Assert.Equal("v1", target.Variables.Single(variable => variable.Key == "version").InitialValue.Value);
        Assert.Contains(target.Variables, variable => variable.Key == "region");
        Assert.Equal(new[] { "dev", "qa" }, target.Environments.Select(environment => environment.Name));
        Assert.Equal(0, result.Overwritten);
        Assert.Equal(1, result.Merged);
    }

    [Fact]
    public void Apply_WithOverwrite_ReplacesInPlaceAndKeepsIdsAndTheActiveEnvironment()
    {
        var (target, admin, purge, list, dev) = Target();
        var source = Source();
        var purgeId = purge.Id;
        var listId = list.Id;
        var result = CollectionMerge.Apply(source, target, All(source), overwrite: true, importEnvironments: true);

        var replaced = target.Requests.Single(request => request.Id == purgeId);
        Assert.Equal("https://new.test/purge", replaced.Url);
        Assert.Equal(HttpMethod.Delete, replaced.Method);
        Assert.Equal(admin.Id, replaced.FolderId);
        Assert.Equal(4, replaced.SortOrder);
        Assert.Contains(purgeId, result.OverwrittenRequestIds);
        Assert.Equal("https://new.test/list", target.Requests.Single(request => request.Id == listId).Url);
        Assert.Equal("v2", target.Variables.Single(variable => variable.Key == "version").InitialValue.Value);

        var environment = Assert.Single(target.Environments, item => item.Name == "dev");
        Assert.Same(dev, environment);
        Assert.Equal(dev.Id, target.ActiveEnvironmentId);
        Assert.True(environment.IsProduction);
        var token = Assert.Single(environment.Variables);
        Assert.True(token.IsSecret);
        Assert.Equal("s3cret", token.InitialValue.Value);
    }

    [Fact]
    public void Apply_SkipsUntickedItemsAndEnvironmentsWhenNotImportingThem()
    {
        var (target, _, _, _, _) = Target();
        var source = Source();
        var included = All(source);
        included.Remove(source.Folders[1].Id);                 // Reports and its request
        var result = CollectionMerge.Apply(source, target, included, overwrite: false, importEnvironments: false);
        Assert.DoesNotContain(target.Folders, folder => folder.Name == "Reports");
        Assert.DoesNotContain(target.Requests, request => request.Name == "Daily");
        Assert.Single(target.Environments);
        Assert.True(result.Skipped > 0);
    }

    [Fact]
    public void ASecondIdenticalImport_CreatesAndOverwritesNothing()
    {
        var (target, _, _, _, _) = Target();
        var source = Source();
        CollectionMerge.Apply(source, target, All(source), overwrite: false, importEnvironments: true);
        var counts = (target.Folders.Count, target.Requests.Count, target.Variables.Count, target.Environments.Count);

        var plan = CollectionMerge.Plan(source, target, overwrite: false);
        Assert.All(plan.Values, action => Assert.Contains(action, new[] { ImportAction.Skip, ImportAction.Merge }));
        Assert.All(source.Requests, request => Assert.Equal(ImportAction.Skip, plan[request.Id]));
        var second = CollectionMerge.Apply(source, target, All(source), overwrite: false, importEnvironments: true);
        Assert.Equal(0, second.Created);
        Assert.Equal(0, second.Overwritten);
        Assert.Equal(counts, (target.Folders.Count, target.Requests.Count, target.Variables.Count, target.Environments.Count));
    }
}
