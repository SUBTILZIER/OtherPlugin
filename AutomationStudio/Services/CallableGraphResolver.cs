namespace AutomationStudioWpf.Services;

public sealed class CallableGraphResolver
{
    public IReadOnlyList<CallableGraphItem> ResolveFunctions(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel? activeAsset)
    {
        var result = new List<CallableGraphItem>();
        if (activeAsset?.Kind == ContentAssetKind.Script)
        {
            result.AddRange(activeAsset.Functions.Select(function =>
                new CallableGraphItem(function.Id, function.Name, "本脚本函数", function.Graph)));
        }
        else if (activeAsset?.Kind == ContentAssetKind.FunctionLibrary)
        {
            result.AddRange(activeAsset.Functions.Select(function =>
                new CallableGraphItem(function.Id, function.Name, "本函数库", function.Graph)));
        }

        foreach (var library in assets.Where(asset =>
                     asset.Kind == ContentAssetKind.FunctionLibrary &&
                     !IsSameAsset(asset, activeAsset)))
        {
            result.AddRange(library.Functions
                .Where(function => function.IsPublicToLibrary)
                .Select(function => new CallableGraphItem(function.Id, $"{library.Name}/{function.Name}", "函数库", function.Graph)));
        }

        return Deduplicate(result);
    }

    public IReadOnlyList<CallableGraphItem> ResolveMacros(
        IEnumerable<ContentAssetViewModel> assets,
        ContentAssetViewModel? activeAsset)
    {
        var result = new List<CallableGraphItem>();
        if (activeAsset?.Kind == ContentAssetKind.Script)
        {
            result.AddRange(activeAsset.Macros.Select(macro =>
                new CallableGraphItem(macro.Id, macro.Name, "本脚本宏", macro.Graph)));
        }
        else if (activeAsset?.Kind == ContentAssetKind.MacroLibrary)
        {
            result.AddRange(activeAsset.Macros.Select(macro =>
                new CallableGraphItem(macro.Id, macro.Name, "本宏库", macro.Graph)));
        }

        foreach (var library in assets.Where(asset =>
                     asset.Kind == ContentAssetKind.MacroLibrary &&
                     !IsSameAsset(asset, activeAsset)))
        {
            result.AddRange(library.Macros
                .Where(macro => macro.IsPublicToLibrary)
                .Select(macro => new CallableGraphItem(macro.Id, $"{library.Name}/{macro.Name}", "宏库", macro.Graph)));
        }

        return Deduplicate(result);
    }

    private static IReadOnlyList<CallableGraphItem> Deduplicate(IEnumerable<CallableGraphItem> items) =>
        items
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

    private static bool IsSameAsset(ContentAssetViewModel? left, ContentAssetViewModel? right) =>
        left is not null &&
        right is not null &&
        string.Equals(left.Id, right.Id, StringComparison.Ordinal);
}
