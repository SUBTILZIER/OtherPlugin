using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphLibraryMapperTests
{
    [TestMethod]
    public void SaveMappingRemovesStartFromAuxiliaryEventGraphs()
    {
        var script = TestGraphFactory.Script("Script A");
        var main = TestGraphFactory.Graph(
            "Main",
            GraphAssetKind.EventGraph,
            GraphEntryRole.MainEvent,
            TestGraphFactory.Node("main-start", "start"));
        var auxiliary = TestGraphFactory.Graph(
            "Auxiliary",
            GraphAssetKind.EventGraph,
            GraphEntryRole.AuxiliaryEvent,
            TestGraphFactory.Node("aux-start", "start"));
        script.EventGraphs.Add(main);
        script.EventGraphs.Add(auxiliary);

        ContentAssetModel mapped = GraphLibraryMapper.ToContentAssetModel(script);

        Assert.AreEqual(1, mapped.EventGraphs[0].Graph.Nodes.Count);
        Assert.AreEqual(0, mapped.EventGraphs[1].Graph.Nodes.Count);
        Assert.AreEqual(GraphEntryRole.MainEvent, mapped.EventGraphs[0].EntryRole);
        Assert.AreEqual(GraphEntryRole.AuxiliaryEvent, mapped.EventGraphs[1].EntryRole);
        Assert.AreEqual(1, auxiliary.Graph.Nodes.Count);
        Assert.AreEqual(GraphEntryRole.AuxiliaryEvent, auxiliary.Graph.EntryRole);
    }

    [TestMethod]
    public void LoadMappingKeepsScriptSettingsAndFunctionVisibility()
    {
        var state = new GraphLibraryState();
        var script = new ContentAssetModel
        {
            Id = "script-1",
            Name = "Script A",
            Kind = ContentAssetKind.Script,
            IsScriptEnabled = false,
            RunSettings = new ScriptRunSettings { LoopCount = 3 },
            Functions =
            [
                new GraphLibraryItem
                {
                    Id = "function-1",
                    Name = "Function A",
                    IsPublicToLibrary = true,
                    Graph = new GraphFileModel { AssetKind = GraphAssetKind.Function },
                },
            ],
        };
        state.ContentAssets.Add(script);

        ContentAssetViewModel restored = GraphLibraryMapper.ToContentAssetViewModel(script);

        Assert.IsFalse(restored.IsScriptEnabled);
        Assert.AreEqual(3, restored.RunSettings.LoopCount);
        Assert.IsTrue(restored.Functions.Single().IsPublicToLibrary);
    }
}
