using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ContentAssetDeletionPlannerTests
{
    [TestMethod]
    public void DeleteFolderContentsIncludesNestedFoldersAndAssets()
    {
        var root = Folder("root", null);
        var childFolder = Folder("child-folder", "root");
        var directAsset = Asset("direct-asset", "root");
        var nestedAsset = Asset("nested-asset", "child-folder");
        var all = new[] { root, childFolder, directAsset, nestedAsset };

        var plan = ContentAssetDeletionPlanner.Build(all, [root], deleteFolderContents: true);

        CollectionAssert.AreEquivalent(
            new[] { "root", "child-folder", "direct-asset", "nested-asset" },
            plan.DeleteIds.ToArray());
        Assert.AreEqual(0, plan.ReparentById.Count);

        var duplicateTargetPlan = ContentAssetDeletionPlanner.Build(all, [root, childFolder], deleteFolderContents: true);
        CollectionAssert.AreEquivalent(plan.DeleteIds.ToArray(), duplicateTargetPlan.DeleteIds.ToArray());
    }

    [TestMethod]
    public void DeleteFolderOnlyMovesDirectChildrenUpAndPreservesNestedHierarchy()
    {
        var root = Folder("root", "parent");
        var childFolder = Folder("child-folder", "root");
        var directAsset = Asset("direct-asset", "root");
        var nestedAsset = Asset("nested-asset", "child-folder");
        var all = new[] { root, childFolder, directAsset, nestedAsset };

        var plan = ContentAssetDeletionPlanner.Build(all, [root], deleteFolderContents: false);

        CollectionAssert.AreEquivalent(new[] { "root" }, plan.DeleteIds.ToArray());
        Assert.AreEqual("parent", plan.ReparentById["child-folder"]);
        Assert.AreEqual("parent", plan.ReparentById["direct-asset"]);
        Assert.AreEqual(2, plan.ReparentById.Count);
        Assert.AreEqual("child-folder", nestedAsset.ParentFolderId);
    }

    [TestMethod]
    public void DescendantCycleDoesNotLoopOrDuplicateIds()
    {
        var first = Folder("first", "second");
        var second = Folder("second", "first");

        var plan = ContentAssetDeletionPlanner.Build([first, second], [first], deleteFolderContents: true);

        CollectionAssert.AreEquivalent(new[] { "first", "second" }, plan.DeleteIds.ToArray());
    }

    [TestMethod]
    public void LegacyFavoriteFilterTokenIsRemovedDuringSettingsNormalization()
    {
        var settings = new AppSettings
        {
            ContentBrowserFilter = "demo is:favorite type:script",
        };

        settings.Normalize();

        Assert.AreEqual("demo type:script", settings.ContentBrowserFilter);
    }

    private static ContentAssetViewModel Folder(string id, string? parentId) => new()
    {
        Id = id,
        Name = id,
        Kind = ContentAssetKind.Folder,
        ParentFolderId = parentId,
    };

    private static ContentAssetViewModel Asset(string id, string? parentId) => new()
    {
        Id = id,
        Name = id,
        Kind = ContentAssetKind.Script,
        ParentFolderId = parentId,
    };
}
