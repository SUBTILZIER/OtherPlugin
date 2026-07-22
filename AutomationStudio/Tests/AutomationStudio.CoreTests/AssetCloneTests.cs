using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class AssetCloneTests
{
    [TestMethod]
    public void CloneRemapsInternalFunctionReferencesWithoutChangingSource()
    {
        var source = TestGraphFactory.Script("Source");
        source.IsScriptEnabled = true;
        var function = TestGraphFactory.ValidFunction("LocalFunction", "source_function");
        var main = GraphStructureNormalizer.CreateMainEventGraph("Main");
        main.Graph.Nodes.Add(new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            Title = "LocalFunction",
            FunctionId = function.Id,
        });
        source.EventGraphs.Add(main);
        source.Functions.Add(function);

        AssetCloneResult result = new AssetCloneService().CloneTree([source], [source], null);
        ContentAssetViewModel clone = result.RootClones.Single();
        string clonedFunctionId = clone.Functions.Single().Id;
        NodeFileModel clonedCall = clone.EventGraphs.Single().Graph.Nodes.Single(node => node.NodeTypeKey == "function_call");

        Assert.AreNotEqual(source.Id, clone.Id);
        Assert.AreNotEqual(function.Id, clonedFunctionId);
        Assert.AreEqual(clonedFunctionId, clonedCall.FunctionId);
        Assert.AreEqual("source_function", source.EventGraphs.Single().Graph.Nodes.Single(node => node.Id == "call").FunctionId);
        Assert.IsFalse(clone.IsScriptEnabled);
    }
}
