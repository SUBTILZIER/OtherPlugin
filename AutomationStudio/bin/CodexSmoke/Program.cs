using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutomationStudioWpf;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Runtime;
using AutomationStudioWpf.Services;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var appData = Path.Combine(AppContext.BaseDirectory, "appdata");
        if (Directory.Exists(appData))
            Directory.Delete(appData, recursive: true);
        Directory.CreateDirectory(appData);
        Environment.SetEnvironmentVariable("APPDATA", appData);
        Environment.SetEnvironmentVariable("AUTOMATION_STUDIO_LIBRARY_DIR", appData);

        var app = new Application();
        try
        {
            var window = new MainWindow();
            CheckFreshContentLibrary(window);
            ResetContent(window);
            CheckContentBrowser(window);
            CheckGraphSectionsAndDirty(window);
            CheckCompileSync();
            CheckCustomEvents();
            CheckLibraryPublishFlag(window);
            Console.WriteLine("Smoke OK");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            app.Shutdown();
        }
    }

    private static void CheckContentBrowser(MainWindow window)
    {
        var assets = window.ContentBrowserItems;
        Assert(assets.Count == 1 && assets[0].Kind == ContentAssetKind.Script, "seeded library has one script asset");
        Assert(window.ContentVisibleItems.Count == 1, "root folder shows seeded script tile");
        Assert(window.ContentFolderItems.Count == 1, "seeded folder tree only has root");
        CheckContentBrowserHeader(window);
        CheckContentBrowserSplitter(window);
        CheckContentBrowserContextMenuModes(window);

        Invoke(window, "AddContentAsset", ContentAssetKind.Folder, "Folder");
        var folder = assets.First(item => item.Kind == ContentAssetKind.Folder);
        Assert(!folder.HasFolderChildren, "new empty folder has no tree arrow");
        Assert(folder.TreeIndent.Left == 16, "first-level folder tree arrow is indented by depth");
        Assert(folder.TreeDisplayName == folder.Name, "tree display name no longer uses string-space indentation");

        Invoke(window, "EnterContentFolder", folder);
        Invoke(window, "AddContentAsset", ContentAssetKind.Script, "Script");
        Assert(window.ContentVisibleItems.All(item => item.ParentFolderId == folder.Id), "current folder filters visible tiles");
        Assert(!folder.HasFolderChildren, "folder with only asset children has no tree arrow");

        Invoke(window, "AddContentAsset", ContentAssetKind.Folder, "Nested");
        var nested = assets.First(item => item.Kind == ContentAssetKind.Folder && item.ParentFolderId == folder.Id);
        Assert(folder.HasFolderChildren, "folder with child folder has tree arrow");
        Assert(nested.TreeIndent.Left == 32, "nested folder arrow follows folder depth");
        CheckFolderToggleGeometry(window, folder);

        Invoke(window, "EnterContentFolder", (object?)null);
        Invoke(window, "ContentFolderItem_PreviewMouseLeftButtonDown", new ListBoxItem { DataContext = folder }, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
        });
        Assert(window.ContentVisibleItems.Count > 0 && window.ContentVisibleItems.All(item => item.ParentFolderId == folder.Id), "single-click folder tree row enters folder");

        Invoke(window, "EnterContentFolder", (object?)null);
        bool wasExpanded = folder.IsTreeExpanded;
        Invoke(window, "ContentFolderToggle_Click", new Button { DataContext = folder }, new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert(folder.IsTreeExpanded != wasExpanded, "folder arrow click toggles expansion");
        Assert(window.ContentVisibleItems.Any(item => item.ParentFolderId is null), "folder arrow click does not enter folder");

        folder.IsTreeExpanded = true;
        Invoke(window, "RefreshContentBrowserViews");

        CheckSameLevelUniqueNames(window, folder);
        CheckFolderSelectionPriority(window, folder);
        CheckCopyIds(window, folder);
        CheckMoveCopyActions(window, folder);
        CheckDropDialogLabels(window);
        CheckContextMenus(window);
    }

    private static void CheckContentBrowserHeader(MainWindow window)
    {
        var header = Get<StackPanel>(window, "ContentBrowserHeaderBar");
        Assert(!header.Children.OfType<Button>().Any(), "content browser header has no create buttons");
    }

    private static void CheckContentBrowserSplitter(MainWindow window)
    {
        var treeColumn = Get<ColumnDefinition>(window, "ContentTreeColumn");
        Assert(treeColumn.Width.Value == 180, "content tree default width is wider than before");
        Assert(treeColumn.MinWidth == 120, "content tree has usable minimum width");
        Assert(treeColumn.MaxWidth == 420, "content tree has bounded maximum width");
        Assert(Get<GridSplitter>(window, "ContentBrowserTreeSplitter").Width == 4, "content browser has tree/tile splitter");
        Assert(Get<ListBox>(window, "ContentBrowserListBox").GetValue(Grid.ColumnProperty) is 2, "asset tiles are right of splitter");
    }

    private static void CheckContentBrowserContextMenuModes(MainWindow window)
    {
        var browser = Get<ListBox>(window, "ContentBrowserListBox");
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);

        Invoke(window, "ContentBrowserListBox_PreviewMouseRightButtonDown", browser, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right));
        Invoke(window, "ContentBrowserContextMenu_Opened", browser.ContextMenu!, new RoutedEventArgs());
        AssertVisibleMenuHeaders(browser.ContextMenu!, ["脚本", "文件夹", "函数库", "宏库"], "blank content browser context menu only shows create options");

        Invoke(window, "ContentAsset_PreviewMouseRightButtonDown", new ListBoxItem { DataContext = script }, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right));
        Invoke(window, "ContentBrowserContextMenu_Opened", browser.ContextMenu!, new RoutedEventArgs());
        AssertVisibleMenuHeaders(browser.ContextMenu!, ["重命名", "删除"], "asset content browser context menu only shows edit options");
    }

    private static void CheckFolderToggleGeometry(MainWindow window, ContentAssetViewModel folder)
    {
        var style = window.Resources["ContentFolderToggleIconStyle"] as Style
            ?? throw new InvalidOperationException("folder toggle icon style missing");
        var defaultData = style.Setters.OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == System.Windows.Shapes.Path.DataProperty)?.Value;
        var expandedData = style.Triggers.OfType<DataTrigger>()
            .SelectMany(trigger => trigger.Setters.OfType<Setter>())
            .FirstOrDefault(setter => setter.Property == System.Windows.Shapes.Path.DataProperty)?.Value;

        var collapsedBounds = GeometryBounds(defaultData);
        var expandedBounds = GeometryBounds(expandedData);
        Assert(collapsedBounds.Height > collapsedBounds.Width, "collapsed folder arrow points right");
        Assert(expandedBounds.Width > expandedBounds.Height, "expanded folder arrow points down");
    }

    private static void CheckFreshContentLibrary(MainWindow window)
    {
        Assert(window.ContentBrowserItems.Count == 0, "fresh library starts with no content assets");
        Assert(window.ContentVisibleItems.Count == 0, "fresh library shows no asset tiles");
        Assert(window.ContentFolderItems.Count == 1, "fresh folder tree shows only root");
    }

    private static void ResetContent(MainWindow window)
    {
        window.ContentBrowserItems.Clear();
        window.ContentBrowserItems.Add(new ContentAssetViewModel
        {
            Kind = ContentAssetKind.Script,
            Name = "Default Script",
        });
        Invoke(window, "RefreshContentBrowserViews");
    }

    private static void CheckSameLevelUniqueNames(MainWindow window, ContentAssetViewModel folder)
    {
        var first = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Same", ParentFolderId = folder.Id };
        var second = new ContentAssetViewModel { Kind = ContentAssetKind.Folder, Name = "Same", ParentFolderId = folder.Id };
        window.ContentBrowserItems.Add(first);
        window.ContentBrowserItems.Add(second);

        Invoke(window, "CommitContentAssetRename", second);
        Assert(!string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase), "same-level asset/folder rename is uniqued");

        var rootDuplicate = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Same" };
        window.ContentBrowserItems.Add(rootDuplicate);
        Invoke(window, "CommitContentAssetRename", rootDuplicate);
        Assert(rootDuplicate.Name == "Same", "same name in different folder is allowed");
    }


    private static void CheckFolderSelectionPriority(MainWindow window, ContentAssetViewModel folder)
    {
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script && item.ParentFolderId is null);
        Get<ListBox>(window, "ContentBrowserListBox").SelectedItem = script;
        Get<ListBox>(window, "ContentFolderListBox").SelectedItem = folder;
        Invoke(window, "ContentFolder_PreviewMouseRightButtonDown", new ListBoxItem { DataContext = folder }, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right));

        var selected = Invoke(window, "GetSelectedContentAsset") as ContentAssetViewModel;
        Assert(ReferenceEquals(selected, folder), "folder-focused selection does not use stale tile selection");
    }

    private static void CheckCopyIds(MainWindow window, ContentAssetViewModel targetFolder)
    {
        var source = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "CopySource" };
        var graph = new GraphListItemViewModel
        {
            Kind = GraphAssetKind.EventGraph,
            Name = "Event",
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes = [new NodeFileModel { Id = "node_a", NodeTypeKey = "start" }],
            },
        };
        source.EventGraphs.Add(graph);

        var clone = (ContentAssetViewModel)(Invoke(window, "CloneContentAssetForCopy", source, targetFolder.Id)
            ?? throw new InvalidOperationException("clone returned null"));

        Assert(clone.Id != source.Id, "copied asset gets new content id");
        Assert(clone.ParentFolderId == targetFolder.Id, "copied asset targets folder");
        Assert(clone.EventGraphs.Count == 1 && clone.EventGraphs[0].Id != graph.Id, "copied graph gets new graph id");
        Assert(clone.EventGraphs[0].Graph.Nodes.Count == 1 && clone.EventGraphs[0].Graph.Nodes[0].Id == "node_a", "copied graph preserves node data");
    }

    private static void CheckMoveCopyActions(MainWindow window, ContentAssetViewModel targetFolder)
    {
        var method = typeof(MainWindow).GetMethod("ApplyContentDropAction", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "ApplyContentDropAction");
        var enumType = method.GetParameters()[2].ParameterType;
        var move = Enum.Parse(enumType, "Move");
        var copy = Enum.Parse(enumType, "Copy");
        var cancel = Enum.Parse(enumType, "Cancel");

        var movable = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Movable" };
        window.ContentBrowserItems.Add(movable);
        method.Invoke(window, [movable, targetFolder.Id, move]);
        Assert(movable.ParentFolderId == targetFolder.Id && movable.IsDirty, "move action updates folder and dirty state");

        var copySource = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "CopyActionSource" };
        copySource.EventGraphs.Add(new GraphListItemViewModel { Kind = GraphAssetKind.EventGraph, Name = "Event" });
        window.ContentBrowserItems.Add(copySource);
        int beforeCopy = window.ContentBrowserItems.Count;
        method.Invoke(window, [copySource, targetFolder.Id, copy]);
        Assert(window.ContentBrowserItems.Count == beforeCopy + 1, "copy action adds an asset");
        Assert(window.ContentBrowserItems.Last().Id != copySource.Id, "copy action asset id does not conflict");

        int beforeCancel = window.ContentBrowserItems.Count;
        method.Invoke(window, [copySource, targetFolder.Id, cancel]);
        Assert(window.ContentBrowserItems.Count == beforeCancel, "cancel action does not change assets");
    }

    private static void CheckDropDialogLabels(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("CreateContentDropDialogContent", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "CreateContentDropDialogContent");
        var actionType = method.GetParameters()[1].ParameterType;
        var enumType = actionType.GetGenericArguments()[0];
        var noop = typeof(Program).GetMethod(nameof(Noop), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(enumType);
        var callback = Delegate.CreateDelegate(actionType, noop);
        var content = (FrameworkElement)(method.Invoke(null, ["Asset", callback])
            ?? throw new InvalidOperationException("drop dialog content returned null"));
        var labels = FindVisualChildren<Button>(content).Select(button => button.Content?.ToString()).ToArray();
        Assert(labels.Contains("移动到此处"), "drop dialog has move label");
        Assert(labels.Contains("复制到此处"), "drop dialog has copy label");
        Assert(labels.Contains("取消"), "drop dialog has cancel label");
    }

    private static void CheckContextMenus(MainWindow window)
    {
        Assert(Get<ListBox>(window, "ContentBrowserListBox").ContextMenu?.Style is not null, "content browser uses shared dark context menu style");
        Assert(Get<ListBox>(window, "ContentFolderListBox").ContextMenu?.Style is not null, "content folder tree uses shared dark context menu style");
        Assert(Get<ListBox>(window, "FunctionListBox").ContextMenu?.Style is not null, "function list uses shared dark context menu style");
        Assert(Get<ListBox>(window, "MacroListBox").ContextMenu?.Style is not null, "macro list uses shared dark context menu style");
        Assert(Get<ListBox>(window, "GraphListBox").ContextMenu?.Style is not null, "event graph list uses shared dark context menu style");

        var style = window.Resources["DarkContextMenuStyle"] as Style
            ?? throw new InvalidOperationException("dark context menu style missing");
        Assert(HasSetter(style, ContextMenu.BackgroundProperty, "#1B2028"), "context menu background is dark");
        Assert(HasSetter(style, ContextMenu.BorderBrushProperty, "#303744"), "context menu border is dark");

        var menuItemStyle = window.Resources[typeof(MenuItem)] as Style
            ?? throw new InvalidOperationException("menu item style missing");
        Assert(HasSetter(menuItemStyle, MenuItem.ForegroundProperty, "#E8EDF5"), "menu item foreground is light on dark");
        Assert(HasSetter(style, Control.TemplateProperty), "context menu has custom dark template without default gutter");
        Assert(HasSetter(menuItemStyle, Control.TemplateProperty), "menu item has custom dark template without default gutter");

        var separatorStyle = window.Resources[typeof(Separator)] as Style
            ?? throw new InvalidOperationException("separator style missing");
        Assert(HasSetter(separatorStyle, Control.TemplateProperty), "separator has custom dark template");
    }

    private static void Noop<T>(T value)
    {
    }

    private static void CheckGraphSectionsAndDirty(MainWindow window)
    {
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        Invoke(window, "OpenContentAsset", script);

        Assert(window.GraphListItems.Count == 0, "script starts with no event graph");
        Assert(window.FunctionListItems.Count == 0, "script starts with no function");
        Assert(window.MacroListItems.Count == 0, "script starts with no macro");
        Assert(Get<FrameworkElement>(window, "EventGraphPanel").Visibility == Visibility.Visible, "event graph section is separate visible panel");
        Assert(Get<FrameworkElement>(window, "FunctionPanel").Visibility == Visibility.Visible, "function section is separate visible panel");
        Assert(Get<FrameworkElement>(window, "MacroPanel").Visibility == Visibility.Visible, "macro section is separate visible panel");
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Collapsed, "empty event graph list is collapsed");
        Assert(Get<ListBox>(window, "FunctionListBox").Visibility == Visibility.Collapsed, "empty function list is collapsed");
        Assert(Get<ListBox>(window, "MacroListBox").Visibility == Visibility.Collapsed, "empty macro list is collapsed");

        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        Assert(window.GraphListItems.Count == 1, "plus creates one event graph");
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Visible, "plus expands event graph list");
        Assert(window.GraphListItems[0].IsCompileDirty, "new graph is compile dirty");

        Invoke(window, "CompileCurrentAssets", false);
        Assert(!window.GraphListItems[0].IsCompileDirty, "compile clears compile dirty");

        Invoke(window, "MarkActiveAssetLayoutDirty");
        Assert(!window.GraphListItems[0].IsCompileDirty, "layout dirty does not set compile dirty");

        Invoke(window, "MarkActiveAssetDirty");
        Assert(window.GraphListItems[0].IsCompileDirty, "logic dirty sets compile dirty");

        Invoke(window, "ToggleEventGraphSection_Click", window, new RoutedEventArgs());
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Collapsed, "toggle collapses non-empty event graph list");
        Invoke(window, "OpenContentAsset", script);
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Collapsed, "runtime collapsed state survives reopen");

        var controller = Get<object>(window, "_graphListController");
        Invoke(controller, "DeleteSelectedConfirmed");
        Invoke(window, "UpdateGraphSectionVisibility");
        Assert(window.GraphListItems.Count == 0, "deleting last event graph does not create replacement graph");
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Collapsed, "deleting last event graph collapses list");
        Assert(!window.Nodes.Cast<object>().Any(), "deleting last event graph clears canvas nodes");

        Invoke(window, "AddFunctionListItem_Click", window, new RoutedEventArgs());
        var fn = window.FunctionListItems.Single();
        fn.Name = "test";
        Invoke(Get<object>(window, "_functionListController"), "CommitRename", fn);
        Assert(fn.Graph.Nodes.First(node => node.NodeTypeKey == "function_entry").Title == "test开始", "function start node title follows graph name");

        Invoke(window, "AddMacroListItem_Click", window, new RoutedEventArgs());
        var macro = window.MacroListItems.Single();
        macro.Name = "test2";
        Invoke(Get<object>(window, "_macroListController"), "CommitRename", macro);
        Assert(macro.Graph.Nodes.First(node => node.NodeTypeKey == "macro_entry").Title == "test2开始", "macro start node title follows graph name");

        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        var evt = window.GraphListItems.Single();
        evt.Name = "event";
        Invoke(Get<object>(window, "_graphListController"), "CommitRename", evt);

        ActivateGraphItem(window, "_functionListController", fn);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.FunctionEntry), "single click function activates function canvas");
        Assert(!window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "function canvas does not contain event start");

        ActivateGraphItem(window, "_macroListController", macro);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.MacroEntry), "single click macro activates macro canvas");
        Assert(!window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.FunctionEntry), "macro canvas does not contain function entry");

        ActivateGraphItem(window, "_graphListController", evt);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "single click event activates event canvas");
        Assert(evt.Graph.Nodes.Any(node => node.NodeTypeKey == "start"), "event graph model remains event-only");
        Assert(fn.Graph.Nodes.Any(node => node.NodeTypeKey == "function_entry"), "function graph model remains function-only");
        Assert(macro.Graph.Nodes.Any(node => node.NodeTypeKey == "macro_entry"), "macro graph model remains macro-only");

        ActivateGraphItem(window, "_functionListController", fn);
        Invoke(window, "OpenContentAsset", script);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "reopening script loads event graph by default");
        Assert(!window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind is NodeKind.FunctionEntry or NodeKind.FunctionReturn or NodeKind.MacroEntry), "reopening script event canvas does not mix function or macro nodes");
    }

    private static void ActivateGraphItem(MainWindow window, string controllerFieldName, GraphListItemViewModel item)
    {
        var controller = Get<object>(window, controllerFieldName);
        var container = new ListBoxItem { DataContext = item };
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left);
        Invoke(window, "ActivateGraphListItem", controller, container, args);
    }

    private static void CheckCompileSync()
    {
        var parameter = new GraphParameterFileModel { Id = "param_a", Name = "InputA", Type = GraphParameterType.String };
        var function = new GraphListItemViewModel
        {
            Name = "Fn",
            Kind = GraphAssetKind.Function,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.Function,
                Nodes =
                [
                    new NodeFileModel
                    {
                        Id = "function_entry",
                        NodeTypeKey = "function_entry",
                        Parameters = [parameter],
                    },
                ],
            },
            IsCompileDirty = true,
        };
        var library = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "Lib" };
        library.Functions.Add(function);

        var call = new NodeFileModel
        {
            Id = "call",
            NodeTypeKey = "function_call",
            FunctionId = function.Id,
        };
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Script" };
        script.EventGraphs.Add(new GraphListItemViewModel
        {
            Name = "Event",
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes = [call],
            },
            IsCompileDirty = true,
        });

        var result = new GraphCompileService().Compile([library, script]);
        Assert(result.UpdatedCallNodes > 0, "compile sync updates call node pins");
        Assert(result.ChangedAssetIds.Contains(script.Id), "compile marks only affected asset dirty");
        Assert(call.InputParameters.Count == 1 && call.InputParameters[0].Id == parameter.Id, "call node input pins match function signature");
        Assert(!function.IsCompileDirty && !script.EventGraphs[0].IsCompileDirty, "compile clears dirty graph flags");
    }

    private static void CheckCustomEvents()
    {
        var parameter = new GraphParameterDefinition
        {
            Id = "event_param",
            Name = "Text",
            Type = GraphParameterType.String,
        };
        var customEvent = new CustomEventNodeViewModel("event_node", "event_id")
        {
            Title = "MyEvent",
            X = 10,
            Y = 20,
        };
        customEvent.Parameters.Add(parameter);
        customEvent.SyncPins();

        var call = new CustomEventCallNodeViewModel("call_node", customEvent.CustomEventId, "OldName")
        {
            X = 100,
            Y = 120,
        };
        call.ConfigurePins([]);

        var customEventFile = NodeSerializer.ToFileModel(customEvent);
        var callFile = NodeSerializer.ToFileModel(call);
        var reloadedEvent = NodeSerializer.FromFileModel(customEventFile) as CustomEventNodeViewModel
            ?? throw new InvalidOperationException("custom event reload failed");
        var reloadedCall = NodeSerializer.FromFileModel(callFile) as CustomEventCallNodeViewModel
            ?? throw new InvalidOperationException("custom event call reload failed");
        Assert(reloadedEvent.CustomEventId == customEvent.CustomEventId, "custom event id survives serialization");
        Assert(reloadedCall.CustomEventId == customEvent.CustomEventId, "custom event call target id survives serialization");

        var graph = new GraphFileModel
        {
            AssetKind = GraphAssetKind.EventGraph,
            Nodes = [customEventFile, callFile],
        };
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Script" };
        script.EventGraphs.Add(new GraphListItemViewModel
        {
            Name = "EventGraph",
            Kind = GraphAssetKind.EventGraph,
            Graph = graph,
            IsCompileDirty = true,
        });

        var result = new GraphCompileService().Compile([script]);
        Assert(result.UpdatedCallNodes > 0, "compile sync updates custom event call");
        Assert(callFile.Title == customEvent.Title, "custom event call title follows event title");
        Assert(callFile.InputParameters.Count == 1 && callFile.InputParameters[0].Id == parameter.Id, "custom event call inputs follow event parameters");

        var executor = new GraphRuntimeExecutor();
        var successPlan = new GraphExecutionPlan(
            [
                GraphRuntimeNode.ForStart("start", "Start"),
                GraphRuntimeNode.ForCustomEvent("event_node", "MyEvent", "event_id"),
                GraphRuntimeNode.ForCustomEventCall("call_node", "MyEvent", "event_id"),
                GraphRuntimeNode.ForPrintLog("event_log", "EventLog", "event"),
                GraphRuntimeNode.ForPrintLog("main_log", "MainLog", "main"),
            ],
            [
                Exec("start", "exec_out", "call_node", "exec_in"),
                Exec("call_node", "exec_out", "main_log", "exec_in"),
                Exec("event_node", "exec_out", "event_log", "exec_in"),
            ]);
        Assert(executor.Execute(successPlan, Environment.CurrentDirectory).Success, "custom event call executes and returns to caller chain");

        var recursivePlan = new GraphExecutionPlan(
            [
                GraphRuntimeNode.ForStart("start", "Start"),
                GraphRuntimeNode.ForCustomEvent("event_node", "MyEvent", "event_id"),
                GraphRuntimeNode.ForCustomEventCall("call_node", "MyEvent", "event_id"),
                GraphRuntimeNode.ForCustomEventCall("recursive_call", "MyEvent", "event_id"),
            ],
            [
                Exec("start", "exec_out", "call_node", "exec_in"),
                Exec("event_node", "exec_out", "recursive_call", "exec_in"),
            ]);
        var recursiveResult = executor.Execute(recursivePlan, Environment.CurrentDirectory);
        Assert(!recursiveResult.Success && recursiveResult.Message.Contains("递归", StringComparison.Ordinal), "custom event recursion is blocked");
    }

    private static void CheckLibraryPublishFlag(MainWindow window)
    {
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        script.Functions.Add(new GraphListItemViewModel
        {
            Name = "LocalFn",
            Kind = GraphAssetKind.Function,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.Function },
        });

        var hiddenFunction = new GraphListItemViewModel
        {
            Name = "HiddenFn",
            Kind = GraphAssetKind.Function,
            IsPublicToLibrary = false,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.Function },
        };
        var publicFunction = new GraphListItemViewModel
        {
            Name = "PublicFn",
            Kind = GraphAssetKind.Function,
            IsPublicToLibrary = true,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.Function },
        };
        var functionLibrary = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "FnLib" };
        functionLibrary.Functions.Add(hiddenFunction);
        functionLibrary.Functions.Add(publicFunction);
        window.ContentBrowserItems.Add(functionLibrary);

        var hiddenMacro = new GraphListItemViewModel
        {
            Name = "HiddenMacro",
            Kind = GraphAssetKind.Macro,
            IsPublicToLibrary = false,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.Macro },
        };
        var publicMacro = new GraphListItemViewModel
        {
            Name = "PublicMacro",
            Kind = GraphAssetKind.Macro,
            IsPublicToLibrary = true,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.Macro },
        };
        var macroLibrary = new ContentAssetViewModel { Kind = ContentAssetKind.MacroLibrary, Name = "MacroLib" };
        macroLibrary.Macros.Add(hiddenMacro);
        macroLibrary.Macros.Add(publicMacro);
        window.ContentBrowserItems.Add(macroLibrary);

        Invoke(window, "OpenContentAsset", script);
        var searchableFunctions = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetCallableFunctions")!).Select(item => item.Name).ToArray();
        Assert(searchableFunctions.Contains("LocalFn"), "script-local function remains searchable");
        Assert(searchableFunctions.Contains("FnLib/PublicFn"), "public library function is searchable");
        Assert(!searchableFunctions.Contains("FnLib/HiddenFn"), "hidden library function is not searchable");

        var searchableMacros = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetCallableMacros")!).Select(item => item.Name).ToArray();
        Assert(searchableMacros.Contains("MacroLib/PublicMacro"), "public library macro is searchable");
        Assert(!searchableMacros.Contains("MacroLib/HiddenMacro"), "hidden library macro is not searchable");

        var runtimeFunctions = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetRuntimeCallableFunctions")!).Select(item => item.Name).ToArray();
        Assert(runtimeFunctions.Contains("FnLib/HiddenFn"), "runtime library keeps hidden function for existing calls");

        Invoke(window, "OpenContentAsset", functionLibrary);
        Assert(window.FunctionListItems.All(item => item.ShowLibraryPublishOption), "function library rows show publish checkbox");
        Invoke(window, "OpenContentAsset", script);
        Assert(window.FunctionListItems.All(item => !item.ShowLibraryPublishOption), "script function rows hide publish checkbox");

        var service = new GraphLibraryService();
        service.SaveContentLibrary([functionLibrary, macroLibrary], functionLibrary.Id);
        var reloaded = service.LoadContentLibrary();
        Assert(reloaded.First(item => item.Kind == ContentAssetKind.FunctionLibrary).Functions.Single(item => item.Name == "PublicFn").IsPublicToLibrary, "function publish flag persists");
        Assert(!reloaded.First(item => item.Kind == ContentAssetKind.MacroLibrary).Macros.Single(item => item.Name == "HiddenMacro").IsPublicToLibrary, "macro hidden flag persists");
    }

    private static GraphRuntimeConnection Exec(string sourceNodeId, string sourcePinName, string targetNodeId, string targetPinName) =>
        new(sourceNodeId, sourcePinName, PinKind.Execution, targetNodeId, targetPinName, PinKind.Execution);

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static T Get<T>(object target, string name) where T : class
    {
        return (T)(target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target)
            ?? throw new MissingFieldException(target.GetType().Name, name));
    }

    private static bool HasSetter(Style style, DependencyProperty property, string color)
    {
        return style.Setters.OfType<Setter>().Any(setter =>
            setter.Property == property &&
            SetterValueMatchesColor(setter.Value, color));
    }

    private static bool HasSetter(Style style, DependencyProperty property)
    {
        return style.Setters.OfType<Setter>().Any(setter => setter.Property == property);
    }

    private static void AssertVisibleMenuHeaders(ContextMenu menu, string[] expected, string message)
    {
        var actual = menu.Items
            .OfType<MenuItem>()
            .Where(item => item.Visibility == Visibility.Visible)
            .Select(item => item.Header?.ToString() ?? string.Empty)
            .ToArray();
        Assert(actual.SequenceEqual(expected), $"{message}: got [{string.Join(", ", actual)}]");
    }

    private static Rect GeometryBounds(object? value)
    {
        var geometry = value switch
        {
            System.Windows.Media.Geometry direct => direct,
            string text => System.Windows.Media.Geometry.Parse(text),
            _ => null,
        };
        return geometry?.Bounds ?? Rect.Empty;
    }

    private static bool SetterValueMatchesColor(object value, string color)
    {
        var expected = color.StartsWith("#", StringComparison.Ordinal) && color.Length == 7
            ? "#FF" + color[1..]
            : color;

        return value switch
        {
            System.Windows.Media.SolidColorBrush brush => brush.Color.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase),
            System.Windows.Media.Color parsedColor => parsedColor.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase),
            string text => text.Equals(color, StringComparison.OrdinalIgnoreCase) || text.Equals(expected, StringComparison.OrdinalIgnoreCase),
            _ => value.ToString()?.Equals(color, StringComparison.OrdinalIgnoreCase) == true ||
                 value.ToString()?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true,
        };
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
                yield return target;

            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
