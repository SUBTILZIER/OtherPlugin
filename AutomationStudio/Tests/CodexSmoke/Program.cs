using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AutomationStudioWpf;
using AutomationStudioWpf.Graph;
using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Logging;
using AutomationStudioWpf.Nodes;
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
            CheckEditorSessions(window);
            CheckFunctionLibrarySessionSnapshot(window);
            CheckPinConnectionPaletteAutoConnect(window);
            CheckConnectionPathHitIsNotBlank(window);
            CheckGraphCommandUndoRedo();
            CheckConnectionPathSelectionDelete();
            CheckPinConnectionStates();
            CheckBatchedConnectionEdits();
            CheckReusableNodeNumbersAndToDoSync();
            CheckToDoInspectorSearch(window);
            CheckToDoRuntimeJumpModes();
            CheckToDoCompileFallbacks();
            CheckNodeDefinitionMetadata();
            CheckGraphSectionsAndDirty(window);
            CheckCompileActiveAsset(window);
            CheckCompileSync();
            CheckCompileAssignsMissingNodeNumbers();
            CheckCompileRejectsPrivateLibraryCalls();
            CheckCompileValidationFailures();
            CheckCustomEvents();
            CheckParameterDefaultValues();
            CheckCallParameterDefaultsAndSync();
            CheckConnectionRebindAfterParameterReorder();
            CheckPinBrushColors();
            CheckRerouteConnectionGeometry();
            CheckExternalRerouteGraphFile();
            CheckDetailsPanelText(window);
            CheckLogRichTextBoxCopy(window);
            CheckLibraryPublishFlag(window);
            CheckSaveAllClearsNestedDirty(window);
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

    private static void CheckEditorSessions(MainWindow window)
    {
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        var library = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "FnLib" };
        window.ContentBrowserItems.Add(library);
        Invoke(window, "RefreshContentBrowserViews");

        Invoke(window, "OpenContentAsset", script);
        Assert(window.EditorSessions.Count == 1, "opening script creates one editor session");
        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "script session shows event graph");

        Invoke(window, "OpenContentAsset", library);
        Assert(window.EditorSessions.Count == 2, "opening another asset keeps first editor session");
        Invoke(window, "AddFunctionListItem_Click", window, new RoutedEventArgs());
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.FunctionEntry), "function library session edits function graph");

        Invoke(window, "OpenContentAsset", script);
        Assert(window.EditorSessions.Count == 2, "reopening script focuses existing session");
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "switching back restores script graph state");

        var scriptSession = window.EditorSessions.First(session => ReferenceEquals(session.ContentAsset, script));
        var librarySession = window.EditorSessions.First(session => ReferenceEquals(session.ContentAsset, library));
        Invoke(window, "CloseEditorSessionsToRight", scriptSession);
        Assert(window.EditorSessions.Count == 1 && !window.EditorSessions.Contains(librarySession), "close right removes right-side editor sessions");
        Assert(window.ContentBrowserItems.Contains(library), "closing editor session does not delete asset");

        Invoke(window, "DetachEditorSession", scriptSession, null);
        Assert(scriptSession.DockMode == EditorDockMode.Detached && scriptSession.DetachedWindow is not null, "detaching creates standalone editor window");
        Assert(window.EditorSessions.Contains(scriptSession), "detached editor remains in all editor sessions");
        Assert(!window.MainEditorSessions.Contains(scriptSession), "detached editor is hidden from main window bar");
        Invoke(window, "CloseMainEditorSessions");
        Assert(window.EditorSessions.Contains(scriptSession), "main close all does not close detached editor sessions");

        Invoke(window, "DockEditorSessionToTab", scriptSession);
        Assert(scriptSession.DockMode == EditorDockMode.Tab && window.MainEditorSessions.Contains(scriptSession), "docking detached editor restores main window tab");

        Invoke(window, "CloseEditorSession", scriptSession);
        Assert(window.EditorSessions.Count == 0, "closing detached editor removes session");
        Assert(window.ContentBrowserItems.Contains(script), "closing detached editor does not delete asset");
    }

    private static void CheckFunctionLibrarySessionSnapshot(MainWindow window)
    {
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        var library = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "FnLibSnapshot" };
        window.ContentBrowserItems.Add(library);
        Invoke(window, "RefreshContentBrowserViews");

        Invoke(window, "OpenContentAsset", script);
        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        var scriptGraph = window.GraphListItems.Single();
        scriptGraph.IsCompileDirty = false;

        Invoke(window, "OpenContentAsset", library);
        Invoke(window, "AddFunctionListItem_Click", window, new RoutedEventArgs());
        var function = window.FunctionListItems.Single();
        function.Name = "FnLatest";
        function.IsPublicToLibrary = true;

        var editor = Get<GraphEditorService>(window, "_editorService");
        var entry = editor.Nodes.OfType<FunctionEntryNodeViewModel>().Single();
        var ret = editor.Nodes.OfType<FunctionReturnNodeViewModel>().Single();
        var log = new PrintLogNodeViewModel("fn_log") { Title = "函数库打印", Message = "latest", X = 420, Y = 120 };
        editor.AddNode(log);
        editor.ClearConnectionsForPin(ret.InputPins.First(pin => pin.Name == "exec_in"));
        editor.CreateConnection(entry.OutputPins.First(pin => pin.Name == "exec_out"), log.InputPins.First(pin => pin.Name == "exec_in"));
        editor.CreateConnection(log.OutputPins.First(pin => pin.Name == "exec_out"), ret.InputPins.First(pin => pin.Name == "exec_in"));

        Invoke(window, "OpenContentAsset", script);
        Invoke(window, "OpenContentAsset", library);
        Assert(editor.Nodes.OfType<PrintLogNodeViewModel>().Any(node => node.Id == "fn_log" && node.Message == "latest"),
            "switching away and back keeps function library edits in the session graph");

        Invoke(window, "OpenContentAsset", script);
        var callable = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetCallableFunctions")!)
            .First(item => item.Name.EndsWith("/FnLatest", StringComparison.Ordinal) || item.Name == "FnLatest");
        Assert(callable.Graph.Nodes.Any(node => node.Id == "fn_log" && node.Text == "latest"),
            "script callable resolver sees unsaved latest function library graph");

        function.IsCompileDirty = true;
        scriptGraph.IsCompileDirty = false;
        Invoke(window, "OpenContentAsset", library);
        Invoke(window, "CompileActiveAsset", false);
        Assert(!function.IsCompileDirty, "compiling active function library clears function dirty");
        Assert(!scriptGraph.IsCompileDirty, "compiling function library does not mark script graph compile dirty");

        Invoke(window, "PersistAssetLibrary");
        var reloaded = Get<GraphLibraryService>(window, "_graphLibraryService").LoadContentLibrary();
        var reloadedFunction = reloaded
            .First(asset => asset.Kind == ContentAssetKind.FunctionLibrary && asset.Name == "FnLibSnapshot")
            .Functions.Single(item => item.Name == "FnLatest");
        Assert(reloadedFunction.Graph.Nodes.Any(node => node.Id == "fn_log" && node.Text == "latest"),
            "persisted function library graph does not revert to default entry/return only");
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
        AssertVisibleMenuHeaders(browser.ContextMenu!, ["脚本", "文件夹", "函数库"], "blank content browser context menu only shows create options");

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
        Invoke(window, "CloseAllEditorSessions");
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
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        Invoke(window, "OpenContentAsset", script);

        Assert(window.GraphListItems.Count == 0, "script starts with no event graph");
        Assert(window.FunctionListItems.Count == 0, "script starts with no function");
        Assert(Get<FrameworkElement>(window, "EventGraphPanel").Visibility == Visibility.Visible, "event graph section is separate visible panel");
        Assert(Get<FrameworkElement>(window, "FunctionPanel").Visibility == Visibility.Visible, "function section is separate visible panel");
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Collapsed, "empty event graph list is collapsed");
        Assert(Get<ListBox>(window, "FunctionListBox").Visibility == Visibility.Collapsed, "empty function list is collapsed");

        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        Assert(window.GraphListItems.Count == 1, "plus creates one event graph");
        Assert(Get<ListBox>(window, "GraphListBox").Visibility == Visibility.Visible, "plus expands event graph list");
        Assert(window.GraphListItems[0].IsCompileDirty, "new graph is compile dirty");

        Invoke(window, "CompileActiveAsset", false);
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

        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        var evt = window.GraphListItems.Single();
        evt.Name = "event";
        Invoke(Get<object>(window, "_graphListController"), "CommitRename", evt);

        ActivateGraphItem(window, "_functionListController", fn);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.FunctionEntry), "single click function activates function canvas");
        Assert(!window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "function canvas does not contain event start");

        ActivateGraphItem(window, "_graphListController", evt);
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "single click event activates event canvas");
        Assert(evt.Graph.Nodes.Any(node => node.NodeTypeKey == "start"), "event graph model remains event-only");
        Assert(fn.Graph.Nodes.Any(node => node.NodeTypeKey == "function_entry"), "function graph model remains function-only");

        ActivateGraphItem(window, "_functionListController", fn);
        int sessionCountBeforeReopen = window.EditorSessions.Count;
        Invoke(window, "OpenContentAsset", script);
        Assert(window.EditorSessions.Count == sessionCountBeforeReopen, "reopening same asset focuses existing editor session");
        Assert(window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.FunctionEntry), "reopening same asset keeps remembered active graph");
        Assert(!window.Nodes.Cast<NodeBaseViewModel>().Any(node => node.NodeKind == NodeKind.Start), "reopening same asset does not reset to event graph");
    }

    private static void CheckCompileActiveAsset(MainWindow window)
    {
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        Invoke(window, "OpenContentAsset", script);

        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        var first = window.GraphListItems.Single();
        Invoke(window, "AddGraphListItem_Click", window, new RoutedEventArgs());
        var second = window.GraphListItems.Last();

        var controller = Get<GraphListController>(window, "_graphListController");
        Assert(first.IsCompileDirty && second.IsCompileDirty, "two new graphs start compile dirty");
        Assert(ReferenceEquals(controller.ActiveItem, second), "second graph is active before compile");

        Invoke(window, "CompileActiveAsset", false);

        Assert(!first.IsCompileDirty, "compile button clears first graph dirty in active asset");
        Assert(!second.IsCompileDirty, "compile button clears active graph dirty in active asset");
    }

    private static void CheckCompileAssignsMissingNodeNumbers()
    {
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "LegacyScript" };
        var eventGraph = new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            IsCompileDirty = true,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                    new NodeFileModel { Id = "log", NodeTypeKey = "print_log" },
                    new NodeFileModel { Id = "route", NodeTypeKey = "reroute", NodeNumber = "N099" },
                ],
            },
        };
        script.EventGraphs.Add(eventGraph);
        var functionLibrary = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "LegacyFunctionLibrary" };
        var functionGraph = new GraphListItemViewModel
        {
            Name = "Function",
            Kind = GraphAssetKind.Function,
            IsCompileDirty = true,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.Function,
                Nodes =
                [
                    new NodeFileModel { Id = "entry", NodeTypeKey = "function_entry" },
                    new NodeFileModel { Id = "return", NodeTypeKey = "function_return" },
                ],
            },
        };
        functionLibrary.Functions.Add(functionGraph);

        var result = new GraphCompileService().Compile([script, functionLibrary]);

        Assert(result.Success, "compile succeeds after assigning missing node numbers");
        Assert(eventGraph.Graph.Nodes.First(node => node.Id == "start").NodeNumber == "N001", "compile assigns first missing event number");
        Assert(eventGraph.Graph.Nodes.First(node => node.Id == "log").NodeNumber == "N002", "compile assigns next missing event number");
        Assert(string.IsNullOrWhiteSpace(eventGraph.Graph.Nodes.First(node => node.Id == "route").NodeNumber), "compile clears reroute node number");
        Assert(functionGraph.Graph.Nodes.First(node => node.Id == "entry").NodeNumber == "Fun001", "compile assigns function entry node number");
        Assert(functionGraph.Graph.Nodes.First(node => node.Id == "return").NodeNumber == "Fun002", "compile assigns function return node number");
        Assert(result.ChangedAssetIds.Contains(script.Id), "node number assignment marks owner asset changed");
        Assert(result.ChangedAssetIds.Contains(functionLibrary.Id), "function number assignment marks owner asset changed");
        Assert(result.Issues.All(issue => !issue.Message.Contains("节点缺少编号", StringComparison.Ordinal)), "compile no longer warns for auto-assigned node numbers");
    }

    private static void CheckPinConnectionPaletteAutoConnect(MainWindow window)
    {
        var editor = Get<GraphEditorService>(window, "_editorService");
        var connector = Get<PinConnectionController>(window, "_pinConnectionController");

        editor.ClearGraph();
        var start = new StartNodeViewModel("start") { X = 80, Y = 80 };
        var log = new PrintLogNodeViewModel("log") { X = 300, Y = 80 };
        editor.AddNode(start);

        var startPin = start.OutputPins.First(pin => pin.Name == "exec_out");
        connector.Begin(startPin, new Point(100, 100));
        connector.Move(new Point(260, 100));
        connector.CompleteOrCancel(null);
        editor.AddNode(log);
        Assert(connector.TryAutoConnectNewNode(log), "wire dropped on blank auto-connects created node from output pin");
        Assert(editor.Connections.Any(connection =>
            connection.SourcePin == startPin &&
            connection.TargetPin.Owner == log &&
            connection.TargetPin.Name == "exec_in"), "output-start auto connection targets new node input");

        editor.ClearGraph();
        var logInputTarget = new PrintLogNodeViewModel("log_input") { X = 300, Y = 80 };
        var newStart = new StartNodeViewModel("new_start") { X = 80, Y = 80 };
        editor.AddNode(logInputTarget);
        var execInput = logInputTarget.InputPins.First(pin => pin.Name == "exec_in");
        connector.Begin(execInput, new Point(300, 100));
        connector.Move(new Point(160, 100));
        connector.CompleteOrCancel(null);
        editor.AddNode(newStart);
        Assert(connector.TryAutoConnectNewNode(newStart), "wire dropped on blank auto-connects created node from input pin");
        Assert(editor.Connections.Any(connection =>
            connection.SourcePin.Owner == newStart &&
            connection.SourcePin.Name == "exec_out" &&
            connection.TargetPin == execInput), "input-start auto connection uses new node output");

        var anchor = execInput.Owner.GetPinAnchor(execInput);
        object?[] args = [new Point(execInput.Owner.X + anchor.X + 18, execInput.Owner.Y + anchor.Y), null];
        var method = typeof(MainWindow).GetMethod("TryGetPinAtPosition", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "TryGetPinAtPosition");
        bool hit = (bool)(method.Invoke(window, args) ?? false);
        Assert(hit && ReferenceEquals(args[1], execInput), "pin hit test accepts near-miss within expanded radius");
    }

    private static void CheckConnectionPathHitIsNotBlank(MainWindow window)
    {
        var editor = Get<GraphEditorService>(window, "_editorService");

        editor.ClearGraph();
        var source = new PrintLogNodeViewModel("source");
        var target = new PrintLogNodeViewModel("target") { X = 360 };
        editor.AddNode(source);
        editor.AddNode(target);
        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        var path = new System.Windows.Shapes.Path { DataContext = editor.ConnectionPaths.Single() };
        bool isBlank = (bool)(Invoke(window, "IsGraphBlankSource", path) ?? true);
        Assert(!isBlank, "connection path hit is not treated as blank canvas");
    }

    private static void CheckGraphCommandUndoRedo()
    {
        var editor = new GraphEditorService();
        var command = new GraphCommandService(editor, () => GraphAssetKind.EventGraph, () => { }, _ => { });
        editor.NewGraph();
        var start = editor.Nodes.OfType<StartNodeViewModel>().Single();
        var log = new PrintLogNodeViewModel("log") { X = 320, Y = 120 };

        command.Execute("Add nodes", () =>
        {
            editor.AddNode(log);
            editor.CreateConnection(start.OutputPins.First(pin => pin.Name == "exec_out"), log.InputPins.First(pin => pin.Name == "exec_in"));
        });
        Assert(editor.Nodes.Count == 2 && editor.Connections.Count == 1, "command execute applies graph edit");

        Assert(command.Undo(), "command undo succeeds");
        Assert(editor.Nodes.Count == 1 && editor.Nodes.Single().NodeKind == NodeKind.Start && editor.Connections.Count == 0,
            "command undo restores previous graph snapshot");

        Assert(command.Redo(), "command redo succeeds");
        Assert(editor.Nodes.Count == 2 && editor.Connections.Count == 1, "command redo restores next graph snapshot");

        var beforeMove = command.Capture();
        editor.Nodes.First(node => node.Id == "log").X = 500;
        command.RecordApplied("Move node", beforeMove, command.Capture());
        Assert(command.Undo(), "recorded move undo succeeds");
        Assert(Math.Abs(editor.Nodes.First(node => node.Id == "log").X - 320) < 0.01, "recorded move undo restores coordinates");
    }

    private static void CheckConnectionPathSelectionDelete()
    {
        var editor = new GraphEditorService();
        var command = new GraphCommandService(editor, () => GraphAssetKind.EventGraph, () => { }, _ => { });
        var source = new StartNodeViewModel("source") { X = 80, Y = 120 };
        var target = new PrintLogNodeViewModel("target") { X = 320, Y = 120 };
        editor.AddNode(source);
        editor.AddNode(target);
        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        var controller = new PinConnectionController(
            editor,
            command,
            new NodeFactory(),
            new Canvas(),
            new System.Windows.Shapes.Path(),
            point => point,
            _ => null,
            _ => { },
            _ => { },
            () => { },
            _ => { },
            _ => { });

        var path = editor.ConnectionPaths.Single();
        path.IsSelected = true;
        Assert(path.SelectionOpacity == 1.0 && path.StrokeThickness > 4.0, "selected connection path exposes highlight state");
        Assert(controller.DeleteSelectedConnectionPath(), "selected connection path can be deleted");
        Assert(editor.Connections.Count == 0, "deleting selected visual path removes backing connections");
        Assert(command.Undo(), "connection path delete is undoable");
        Assert(editor.Connections.Count == 1 && editor.ConnectionPaths.Count == 1, "undo restores deleted connection path");
    }

    private static void CheckPinConnectionStates()
    {
        var editor = new GraphEditorService();
        var source = new StartNodeViewModel("source") { X = 80, Y = 120 };
        var target = new PrintLogNodeViewModel("target") { X = 320, Y = 120 };
        editor.AddNode(source);
        editor.AddNode(target);

        var sourcePin = source.OutputPins.First(pin => pin.Name == "exec_out");
        var targetPin = target.InputPins.First(pin => pin.Name == "exec_in");
        editor.UpdatePinConnectionStates();
        Assert(!sourcePin.HasConnection && !targetPin.HasConnection, "fresh pins start disconnected");

        editor.CreateConnection(sourcePin, targetPin);
        editor.UpdatePinConnectionStates();
        Assert(sourcePin.HasConnection && targetPin.HasConnection, "pin state tracks created connection");

        editor.RemoveConnection(editor.Connections.Single());
        editor.UpdatePinConnectionStates();
        Assert(!sourcePin.HasConnection && !targetPin.HasConnection, "pin state clears after removed connection");
    }

    private static void CheckBatchedConnectionEdits()
    {
        CheckBatchedConnectionReplacement();
        CheckBatchedManualConnectionMutations();
        CheckBatchedMultiReroutePathDelete();
    }

    private static void CheckBatchedConnectionReplacement()
    {
        var editor = new GraphEditorService();
        var source = new StartNodeViewModel("source") { X = 80, Y = 120 };
        var firstTarget = new PrintLogNodeViewModel("first") { X = 320, Y = 80 };
        var secondTarget = new PrintLogNodeViewModel("second") { X = 320, Y = 200 };
        editor.AddNode(source);
        editor.AddNode(firstTarget);
        editor.AddNode(secondTarget);

        var sourcePin = source.OutputPins.First(pin => pin.Name == "exec_out");
        var firstInput = firstTarget.InputPins.First(pin => pin.Name == "exec_in");
        var secondInput = secondTarget.InputPins.First(pin => pin.Name == "exec_in");
        editor.CreateConnection(sourcePin, firstInput);

        int graphChangedCount = 0;
        editor.GraphChanged += () => graphChangedCount++;
        editor.CreateConnection(sourcePin, secondInput);
        editor.UpdatePinConnectionStates();

        Assert(graphChangedCount == 1, "replacing an execution connection raises one graph changed event");
        Assert(editor.Connections.Count == 1 && ReferenceEquals(editor.Connections.Single().TargetPin, secondInput), "replacement keeps only the new execution connection");
        Assert(editor.ConnectionPaths.Count == 1, "replacement rebuilds visible paths once to the final state");
        Assert(!firstInput.HasConnection && secondInput.HasConnection, "replacement refreshes pin states to the final connection");
    }

    private static void CheckBatchedManualConnectionMutations()
    {
        var editor = new GraphEditorService();
        var source = new StartNodeViewModel("source") { X = 80, Y = 120 };
        var reroute = new RerouteNodeViewModel("reroute", PinKind.Execution) { X = 260, Y = 160 };
        var target = new PrintLogNodeViewModel("target") { X = 420, Y = 120 };
        editor.AddNode(source);
        editor.AddNode(reroute);
        editor.AddNode(target);

        int graphChangedCount = 0;
        editor.GraphChanged += () => graphChangedCount++;
        editor.RunBatchedEdit(() =>
        {
            editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), reroute.InputPins.First(pin => pin.Name == "in"));
            editor.CreateConnection(reroute.OutputPins.First(pin => pin.Name == "out"), target.InputPins.First(pin => pin.Name == "exec_in"));
        });

        Assert(graphChangedCount == 1, "manual batched connection mutations raise one graph changed event");
        Assert(editor.Connections.Count == 2, "manual batch applies all backing connections");
        Assert(editor.ConnectionPaths.Count == 1, "manual batch rebuilds one aggregated visual path");
    }

    private static void CheckBatchedMultiReroutePathDelete()
    {
        var editor = new GraphEditorService();
        var command = new GraphCommandService(editor, () => GraphAssetKind.EventGraph, () => { }, _ => { });
        var source = new StartNodeViewModel("source") { X = 80, Y = 120 };
        var routeA = new RerouteNodeViewModel("route_a", PinKind.Execution) { X = 220, Y = 220 };
        var routeB = new RerouteNodeViewModel("route_b", PinKind.Execution) { X = 360, Y = 220 };
        var target = new PrintLogNodeViewModel("target") { X = 520, Y = 120 };
        editor.AddNode(source);
        editor.AddNode(routeA);
        editor.AddNode(routeB);
        editor.AddNode(target);
        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), routeA.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeA.OutputPins.First(pin => pin.Name == "out"), routeB.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeB.OutputPins.First(pin => pin.Name == "out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        var controller = new PinConnectionController(
            editor,
            command,
            new NodeFactory(),
            new Canvas(),
            new System.Windows.Shapes.Path(),
            point => point,
            _ => null,
            _ => { },
            _ => { },
            () => { },
            _ => { },
            _ => { });

        var path = editor.ConnectionPaths.Single();
        path.IsSelected = true;
        int graphChangedCount = 0;
        editor.GraphChanged += () => graphChangedCount++;

        Assert(controller.DeleteSelectedConnectionPath(), "batched selected multi-reroute path can be deleted");
        Assert(graphChangedCount == 1, "batched selected path delete raises one graph changed event");
        Assert(editor.Connections.Count == 0 && editor.ConnectionPaths.Count == 0, "batched path delete removes all backing connections and visible paths");
        Assert(command.Undo(), "batched selected path delete remains undoable");
        Assert(editor.Connections.Count == 3 && editor.ConnectionPaths.Count == 1, "undo restores batched deleted multi-reroute path");
    }

    private static void CheckReusableNodeNumbersAndToDoSync()
    {
        var editor = new GraphEditorService();
        editor.NewGraph();
        var first = new PrintLogNodeViewModel("first") { Title = "First" };
        var second = new PrintLogNodeViewModel("second") { Title = "Second" };
        editor.AddNode(first);
        editor.AddNode(second);

        Assert(editor.Nodes.OfType<StartNodeViewModel>().Single().NodeNumber == "N001", "event graph start uses N prefix");
        Assert(first.NodeNumber == "N002" && second.NodeNumber == "N003", "event graph assigns sequential reusable numbers");
        editor.RemoveNode(first);
        var replacement = new PrintLogNodeViewModel("replacement") { Title = "Replacement" };
        editor.AddNode(replacement);
        Assert(replacement.NodeNumber == "N002", "deleted node number is reused by the next node");

        var toDo = new ToDoNodeViewModel("todo")
        {
            TargetNodeId = replacement.Id,
            TargetNodeTitle = replacement.Title,
            TargetNodeNumber = replacement.NodeNumber,
        };
        editor.AddNode(toDo);
        replacement.Title = "ReplacementRenamed";
        Assert(toDo.TargetNodeTitle == "ReplacementRenamed" && toDo.TargetNodeNumber == replacement.NodeNumber,
            "ToDo target fields sync when referenced node title changes");

        editor.NewFunctionGraph();
        Assert(editor.Nodes.All(node => node.NodeKind == NodeKind.Reroute || node.NodeNumber.StartsWith("Fun", StringComparison.Ordinal)),
            "function graph nodes use Fun prefix");
    }

    private static void CheckToDoInspectorSearch(MainWindow window)
    {
        var editor = Get<GraphEditorService>(window, "_editorService");
        editor.NewGraph();
        var target = new PrintLogNodeViewModel("todo_target") { Title = "SearchTarget" };
        var toDo = new ToDoNodeViewModel("todo_node") { Title = "ToDo跳转" };
        editor.AddNode(target);
        editor.AddNode(toDo);
        foreach (var node in editor.Nodes)
            node.IsSelected = false;
        toDo.IsSelected = true;

        Invoke(window, "LoadNodeToInspector", toDo);
        var searchBox = Get<TextBox>(window, "ToDoSearchBox");
        var listBox = Get<ListBox>(window, "ToDoTargetListBox");
        searchBox.Text = target.NodeNumber;
        FlushDispatcher();

        Assert(listBox.Items.Count == 1, "ToDo search filters by node number");
        listBox.SelectedIndex = 0;
        FlushDispatcher();

        Assert(Get<TextBox>(window, "ToDoTargetTitleTextBox").Text == target.Title, "ToDo picker fills target title");
        Assert(Get<TextBox>(window, "ToDoTargetNumberTextBox").Text == target.NodeNumber, "ToDo picker fills target number");
        Assert(toDo.TargetNodeId == target.Id, "ToDo picker stores maintenance target id");

        var exported = editor.ExportGraphModel("todo-export");
        var exportedToDo = exported.Nodes.Single(node => node.Id == toDo.Id);
        Assert(exportedToDo.TargetNodeTitle == target.Title && exportedToDo.TargetNodeNumber == target.NodeNumber,
            "ToDo picker target persists into exported graph model");

        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "ToDoScript" };
        var graphItem = new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = exported,
            IsCompileDirty = true,
        };
        script.EventGraphs.Add(graphItem);
        var compile = new GraphCompileService().CompileGraph([script], script, graphItem);
        Assert(compile.Success && compile.Issues.All(issue => !issue.Message.Contains("缺少目标节点名或编号", StringComparison.Ordinal)),
            "ToDo selected static target compiles without missing-target error");
    }

    private static void CheckToDoRuntimeJumpModes()
    {
        CheckToDoGotoModeSkipsOwnOutput();
        CheckToDoReturnModeContinuesOwnOutput();
        CheckToDoDoesNotJumpToReusedNumberWithDifferentName();
        CheckToDoConnectedInputsOverrideStaticTarget();
    }

    private static void CheckToDoCompileFallbacks()
    {
        var targetIdOnly = new NodeFileModel
        {
            Id = "target",
            NodeTypeKey = "print_log",
            Title = "IdOnlyTarget",
        };
        var toDoIdOnly = new NodeFileModel
        {
            Id = "todo",
            NodeTypeKey = "todo",
            Title = "ToDo跳转",
            TargetNodeId = targetIdOnly.Id,
        };
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "ToDoFallbackScript" };
        var graphItem = new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start", Title = "事件开始运行" },
                    targetIdOnly,
                    toDoIdOnly,
                ],
            },
            IsCompileDirty = true,
        };
        script.EventGraphs.Add(graphItem);

        var result = new GraphCompileService().CompileGraph([script], script, graphItem);

        Assert(result.Success, "compile fills ToDo title and number from target id");
        Assert(toDoIdOnly.TargetNodeTitle == targetIdOnly.Title && toDoIdOnly.TargetNodeNumber == targetIdOnly.NodeNumber,
            "compile persists ToDo target id fallback into file model");

        var dynamicGraph = new GraphListItemViewModel
        {
            Name = "Dynamic",
            Kind = GraphAssetKind.EventGraph,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start", Title = "事件开始运行" },
                    new NodeFileModel { Id = "title", NodeTypeKey = "string_concat", Title = "TitleSource" },
                    new NodeFileModel { Id = "number", NodeTypeKey = "string_concat", Title = "NumberSource" },
                    new NodeFileModel { Id = "todo_dynamic", NodeTypeKey = "todo", Title = "ToDo跳转" },
                ],
                Connections =
                [
                    FileConn("title", "value", "todo_dynamic", "target_title"),
                    FileConn("number", "value", "todo_dynamic", "target_number"),
                ],
            },
            IsCompileDirty = true,
        };
        script.EventGraphs.Clear();
        script.EventGraphs.Add(dynamicGraph);

        var dynamicResult = new GraphCompileService().CompileGraph([script], script, dynamicGraph);

        Assert(dynamicResult.Success, "compile accepts ToDo dynamic target inputs without static target");
    }

    private static void CheckToDoGotoModeSkipsOwnOutput()
    {
        var editor = CreateToDoRuntimeGraph(returnAfterTarget: false, "target-goto", "after-goto");
        int logStart = Logger.Entries.Count;
        var result = new GraphRuntimeExecutor().Execute(editor.BuildExecutionPlan(), AppContext.BaseDirectory);
        FlushDispatcher();
        var messages = Logger.Entries.Skip(logStart).Select(entry => entry.Message).ToArray();

        Assert(result.Success, "ToDo goto mode executes successfully");
        Assert(messages.Any(message => message.Contains("打印log：target-goto", StringComparison.Ordinal)), "ToDo goto runs target node");
        Assert(!messages.Any(message => message.Contains("打印log：after-goto", StringComparison.Ordinal)), "ToDo goto skips own exec_out");
    }

    private static void CheckToDoReturnModeContinuesOwnOutput()
    {
        var editor = CreateToDoRuntimeGraph(returnAfterTarget: true, "target-return", "after-return");
        int logStart = Logger.Entries.Count;
        var result = new GraphRuntimeExecutor().Execute(editor.BuildExecutionPlan(), AppContext.BaseDirectory);
        FlushDispatcher();
        var messages = Logger.Entries.Skip(logStart).Select(entry => entry.Message).ToArray();

        Assert(result.Success, "ToDo return mode executes successfully");
        Assert(messages.Any(message => message.Contains("打印log：target-return", StringComparison.Ordinal)), "ToDo return runs target node");
        Assert(messages.Any(message => message.Contains("打印log：after-return", StringComparison.Ordinal)), "ToDo return continues own exec_out");
    }

    private static void CheckToDoDoesNotJumpToReusedNumberWithDifferentName()
    {
        var editor = new GraphEditorService();
        editor.NewGraph();
        var oldTarget = new PrintLogNodeViewModel("old_target") { Title = "OldTarget", Message = "old" };
        editor.AddNode(oldTarget);
        string reusedNumber = oldTarget.NodeNumber;
        var toDo = new ToDoNodeViewModel("todo") { TargetNodeTitle = oldTarget.Title, TargetNodeNumber = reusedNumber };
        editor.AddNode(toDo);
        editor.RemoveNode(oldTarget);
        var newTarget = new PrintLogNodeViewModel("new_target") { Title = "NewTarget", Message = "new" };
        editor.AddNode(newTarget);

        var start = editor.Nodes.OfType<StartNodeViewModel>().Single();
        editor.CreateConnection(start.OutputPins.First(pin => pin.Name == "exec_out"), toDo.InputPins.First(pin => pin.Name == "exec_in"));
        var result = new GraphRuntimeExecutor().Execute(editor.BuildExecutionPlan(), AppContext.BaseDirectory);

        Assert(newTarget.NodeNumber == reusedNumber, "replacement target reuses deleted node number");
        Assert(!result.Success && result.Message.Contains("找不到目标", StringComparison.Ordinal), "ToDo requires title and number, not number alone");
    }

    private static void CheckToDoConnectedInputsOverrideStaticTarget()
    {
        int logStart = Logger.Entries.Count;
        var plan = new GraphExecutionPlan(
            [
                GraphRuntimeNode.ForStart("start", "Start"),
                GraphRuntimeNode.ForCommon("title", "TitleSource", NodeKind.StringConcat, "DynamicTarget", string.Empty, string.Empty, 0, 0, 0, 0, false),
                GraphRuntimeNode.ForCommon("number", "NumberSource", NodeKind.StringConcat, "N005", string.Empty, string.Empty, 0, 0, 0, 0, false),
                GraphRuntimeNode.ForToDo("todo", "ToDo跳转", "StaticTarget", "N999", null, false) with { NodeNumber = "N012" },
                GraphRuntimeNode.ForPrintLog("static", "StaticTarget", "static") with { NodeNumber = "N999" },
                GraphRuntimeNode.ForPrintLog("dynamic", "DynamicTarget", "dynamic") with { NodeNumber = "N005" },
            ],
            [
                Exec("start", "exec_out", "title", "exec_in"),
                Exec("title", "exec_out", "number", "exec_in"),
                Exec("number", "exec_out", "todo", "exec_in"),
                StringConn("title", "value", "todo", "target_title"),
                StringConn("number", "value", "todo", "target_number"),
            ]);

        var result = new GraphRuntimeExecutor().Execute(plan, AppContext.BaseDirectory);
        FlushDispatcher();
        var messages = Logger.Entries.Skip(logStart).Select(entry => entry.Message).ToArray();

        Assert(result.Success, "ToDo connected inputs execute successfully");
        Assert(messages.Any(message => message.Contains("打印log：dynamic", StringComparison.Ordinal)), "ToDo connected inputs choose dynamic target");
        Assert(!messages.Any(message => message.Contains("打印log：static", StringComparison.Ordinal)), "ToDo connected inputs override static target");
    }

    private static GraphEditorService CreateToDoRuntimeGraph(bool returnAfterTarget, string targetMessage, string afterMessage)
    {
        var editor = new GraphEditorService();
        editor.NewGraph();
        var target = new PrintLogNodeViewModel("target") { Title = "Target", Message = targetMessage };
        var after = new PrintLogNodeViewModel("after") { Title = "After", Message = afterMessage };
        var toDo = new ToDoNodeViewModel("todo")
        {
            Title = "ToDo",
            ReturnAfterTarget = returnAfterTarget,
        };
        editor.AddNode(target);
        editor.AddNode(after);
        editor.AddNode(toDo);
        toDo.TargetNodeTitle = target.Title;
        toDo.TargetNodeNumber = target.NodeNumber;
        toDo.TargetNodeId = target.Id;

        var start = editor.Nodes.OfType<StartNodeViewModel>().Single();
        editor.CreateConnection(start.OutputPins.First(pin => pin.Name == "exec_out"), toDo.InputPins.First(pin => pin.Name == "exec_in"));
        editor.CreateConnection(toDo.OutputPins.First(pin => pin.Name == "exec_out"), after.InputPins.First(pin => pin.Name == "exec_in"));
        return editor;
    }

    private static void CheckNodeDefinitionMetadata()
    {
        var registry = NodeRegistry.CreateDefault();
        var waitImage = registry.Definitions.Single(def => def.NodeKind == NodeKind.WaitImage);

        Assert(waitImage.SearchTags.Contains("wait_image"), "node definition exposes generated type-key search tag");
        Assert(!string.IsNullOrWhiteSpace(waitImage.InspectorSchemaKey), "node definition exposes inspector schema key");
        Assert((bool)(InvokeStatic(typeof(NodePaletteController), "MatchesFilter", waitImage, "wait") ?? false),
            "node palette search matches generated tags");
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
            IsPublicToLibrary = true,
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
                    new NodeFileModel
                    {
                        Id = "function_return",
                        NodeTypeKey = "function_return",
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
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                    call,
                ],
            },
            IsCompileDirty = true,
        });

        var result = new GraphCompileService().Compile([library, script]);
        Assert(result.UpdatedCallNodes > 0, "compile sync updates call node pins");
        Assert(result.ChangedAssetIds.Contains(script.Id), "compile marks only affected asset dirty");
        Assert(call.InputParameters.Count == 1 && call.InputParameters[0].Id == parameter.Id, "call node input pins match function signature");
        Assert(!function.IsCompileDirty && !script.EventGraphs[0].IsCompileDirty, "compile clears dirty graph flags");
    }

    private static void CheckCompileRejectsPrivateLibraryCalls()
    {
        var hiddenFunction = new GraphListItemViewModel
        {
            Name = "HiddenFn",
            Kind = GraphAssetKind.Function,
            IsPublicToLibrary = false,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.Function,
                Nodes =
                [
                    new NodeFileModel { Id = "function_entry", NodeTypeKey = "function_entry" },
                    new NodeFileModel { Id = "function_return", NodeTypeKey = "function_return" },
                ],
            },
            IsCompileDirty = true,
        };
        var library = new ContentAssetViewModel { Kind = ContentAssetKind.FunctionLibrary, Name = "Lib" };
        library.Functions.Add(hiddenFunction);

        var scriptGraph = new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                    new NodeFileModel { Id = "call", NodeTypeKey = "function_call", FunctionId = hiddenFunction.Id, Title = "HiddenFn" },
                ],
            },
            IsCompileDirty = true,
        };
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Script" };
        script.EventGraphs.Add(scriptGraph);

        var result = new GraphCompileService().Compile([library, script]);
        Assert(!result.Success, "compile rejects private library function from other script");
        Assert(scriptGraph.IsCompileDirty, "failed compile keeps dirty flag");
        Assert(result.Issues.Any(issue => issue.Message.Contains("未公开", StringComparison.Ordinal)), "private library compile issue is explicit");

        hiddenFunction.IsPublicToLibrary = true;
        result = new GraphCompileService().Compile([library, script]);
        Assert(result.Success, "compile accepts function after publishing to library");
        Assert(!scriptGraph.IsCompileDirty, "successful compile clears dirty flag");
    }

    private static void CheckCompileValidationFailures()
    {
        var missingStart = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "MissingStart" };
        missingStart.EventGraphs.Add(new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = new GraphFileModel { AssetKind = GraphAssetKind.EventGraph },
            IsCompileDirty = true,
        });
        var missingStartResult = new GraphCompileService().Compile([missingStart]);
        Assert(!missingStartResult.Success, "compile fails when event graph has no start node");
        Assert(missingStart.EventGraphs[0].IsCompileDirty, "failed missing-start compile keeps dirty");
        Assert(missingStartResult.Issues.Any(issue => issue.Message.StartsWith("content/MissingStart/Event:", StringComparison.Ordinal)),
            "compile issue path starts at content root");

        var duplicateExec = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "DuplicateExec" };
        duplicateExec.EventGraphs.Add(new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.EventGraph,
                Nodes =
                [
                    new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                    new NodeFileModel { Id = "log1", NodeTypeKey = "print_log" },
                    new NodeFileModel { Id = "log2", NodeTypeKey = "print_log" },
                ],
                Connections =
                [
                    FileConn("start", "exec_out", "log1", "exec_in"),
                    FileConn("start", "exec_out", "log2", "exec_in"),
                ],
            },
            IsCompileDirty = true,
        });
        var duplicateExecResult = new GraphCompileService().Compile([duplicateExec]);
        Assert(!duplicateExecResult.Success, "compile fails on duplicate execution output connections");

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
            Nodes =
            [
                new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                customEventFile,
                callFile,
            ],
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

    private static void CheckParameterDefaultValues()
    {
        var entry = new FunctionEntryNodeViewModel("entry");
        entry.Parameters.Add(new GraphParameterDefinition
        {
            Id = "flag",
            Name = "Flag",
            Type = GraphParameterType.Boolean,
            DefaultValue = "False",
        });
        entry.Parameters.Add(new GraphParameterDefinition
        {
            Id = "speed",
            Name = "Speed",
            Type = GraphParameterType.Float,
            DefaultValue = "23.0f",
        });
        entry.SyncPins();

        var file = NodeSerializer.ToFileModel(entry);
        Assert(file.Parameters.First(parameter => parameter.Id == "flag").DefaultValue == "False", "parameter default value serializes");
        Assert(file.Parameters.First(parameter => parameter.Id == "speed").DefaultValue == "23.0f", "float-like parameter default serializes");

        var reloaded = NodeSerializer.FromFileModel(file) as FunctionEntryNodeViewModel
            ?? throw new InvalidOperationException("function entry reload failed");
        Assert(reloaded.Parameters.First(parameter => parameter.Id == "flag").DefaultValue == "False", "parameter default value reloads");
        Assert(reloaded.Parameters.First(parameter => parameter.Id == "speed").DefaultValue == "23.0f", "float-like parameter default reloads");

        var runtimeNode = NodeSerializer.ToRuntimeNode(reloaded);
        var context = new RuntimeContext();
        InvokeStatic(typeof(GraphRuntimeExecutor), "ApplyParameterDefaults", runtimeNode, context, runtimeNode.Id, null);
        Assert(context.TryGetRaw(runtimeNode.Id, "flag", out var flag) && flag is bool boolValue && !boolValue, "boolean default writes runtime value");
        Assert(context.TryGetRaw(runtimeNode.Id, "speed", out var speed) && speed is string text && text == "23.0f", "float-like default writes runtime string value");
    }

    private static void CheckCallParameterDefaultsAndSync()
    {
        var input = new GraphParameterFileModel
        {
            Id = "input",
            Name = "Input",
            Type = GraphParameterType.String,
            DefaultValue = "signature",
        };
        var output = new GraphParameterFileModel
        {
            Id = "result",
            Name = "Result",
            Type = GraphParameterType.Float,
            DefaultValue = "2.5",
        };
        var function = new GraphListItemViewModel
        {
            Name = "Fn",
            Kind = GraphAssetKind.Function,
            IsPublicToLibrary = true,
            Graph = new GraphFileModel
            {
                AssetKind = GraphAssetKind.Function,
                Nodes =
                [
                    new NodeFileModel { Id = "entry", NodeTypeKey = "function_entry", Parameters = [input] },
                    new NodeFileModel { Id = "ret", NodeTypeKey = "function_return", Parameters = [output] },
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
            InputParameters =
            [
                new GraphParameterFileModel
                {
                    Id = "input",
                    Name = "OldInput",
                    Type = GraphParameterType.String,
                    DefaultValue = "local",
                },
            ],
            OutputParameters = [output],
        };
        var scriptGraph = new GraphFileModel
        {
            AssetKind = GraphAssetKind.EventGraph,
            Nodes =
            [
                new NodeFileModel { Id = "start", NodeTypeKey = "start" },
                call,
                new NodeFileModel { Id = "log", NodeTypeKey = "print_log" },
            ],
            Connections =
            [
                FileConn("call", "result", "log", "message"),
            ],
        };
        var script = new ContentAssetViewModel { Kind = ContentAssetKind.Script, Name = "Script" };
        script.EventGraphs.Add(new GraphListItemViewModel
        {
            Name = "Event",
            Kind = GraphAssetKind.EventGraph,
            Graph = scriptGraph,
            IsCompileDirty = true,
        });

        var result = new GraphCompileService().Compile([library, script]);
        Assert(result.Success, "compile succeeds with function output linked to print log");
        Assert(scriptGraph.Connections.Count == 1 && scriptGraph.Connections[0].SourcePinName == "result", "compile preserves return value connections");
        Assert(call.InputParameters.Single().DefaultValue == "local", "compile preserves call-site input default value");

        var reloadedCall = NodeSerializer.FromFileModel(call) as FunctionCallNodeViewModel
            ?? throw new InvalidOperationException("function call reload failed");
        Assert(reloadedCall.InputParameters.Single().DefaultValue == "local", "call-site input default reloads");
        var callFile = NodeSerializer.ToFileModel(reloadedCall);
        Assert(callFile.InputParameters.Single().DefaultValue == "local", "call-site input default serializes");

        var runtimeCall = GraphRuntimeNode.ForFunctionCall(
            "call",
            "Fn",
            function.Id,
            [new GraphParameterDefinition { Id = "input", Type = GraphParameterType.String, DefaultValue = "local" }]);
        var runtimeEntry = GraphRuntimeNode.ForAssetNode(
            "entry",
            "Entry",
            NodeKind.FunctionEntry,
            [new GraphParameterDefinition { Id = "input", Type = GraphParameterType.String, DefaultValue = "signature" }]);
        var context = new RuntimeContext();
        var plan = new GraphExecutionPlan([runtimeCall], []);
        InvokeStatic(typeof(GraphRuntimeExecutor), "CopyCallInputsToEntry", plan, runtimeCall, context, runtimeEntry, context);
        Assert(context.TryGetRaw("entry", "input", out var copied) && copied is string copiedText && copiedText == "local", "runtime uses call-site default before entry default");

        var skippedContext = new RuntimeContext();
        var connectedPlan = new GraphExecutionPlan(
            [runtimeCall],
            [new GraphRuntimeConnection("source", "value", PinKind.String, "call", "input", PinKind.String)]);
        InvokeStatic(typeof(GraphRuntimeExecutor), "CopyCallInputsToEntry", connectedPlan, runtimeCall, skippedContext, runtimeEntry, skippedContext);
        Assert(!skippedContext.TryGetRaw("entry", "input", out _), "connected input without upstream value does not fall back to defaults");
    }

    private static void CheckPinBrushColors()
    {
        Assert(PinBrushes.ForKind(PinKind.Execution).Color.ToString() == "#FFF4F4F4", "execution pin color matches expected UE-like white");
        Assert(PinBrushes.ForKind(PinKind.Boolean).Color.ToString() == "#FFB82D30", "boolean pin color matches expected UE-like red");
        Assert(PinBrushes.ForKind(PinKind.Vector2D).Color.ToString() == "#FF50C472", "vector pin color matches expected UE-like green");
        Assert(PinBrushes.ForKind(PinKind.String).Color.ToString() == "#FFCA2EA5", "string pin color matches expected UE-like magenta");
        Assert((new RerouteNodeViewModel("reroute", PinKind.String).CircleFill as System.Windows.Media.SolidColorBrush)?.Color.ToString() == "#FFCA2EA5",
            "reroute pin color uses shared pin color table");
    }

    private static void CheckRerouteConnectionGeometry()
    {
        var source = new PrintLogNodeViewModel("source") { X = 360, Y = 80 };
        var reroute = new RerouteNodeViewModel("reroute", PinKind.Execution) { X = 220, Y = 250 };
        var target = new PrintLogNodeViewModel("target") { X = 40, Y = 80 };
        var sourcePin = source.OutputPins.First(pin => pin.Name == "exec_out");
        var rerouteInput = reroute.InputPins.First(pin => pin.Name == "in");
        var rerouteOutput = reroute.OutputPins.First(pin => pin.Name == "out");
        var targetPin = target.InputPins.First(pin => pin.Name == "exec_in");

        rerouteInput.AnchorPoint = new Point(3, 10);
        rerouteOutput.AnchorPoint = new Point(17, 10);

        Assert(reroute.GetPinAnchor(rerouteInput) == new Point(10, 10), "reroute input anchor stays centered");
        Assert(reroute.GetPinAnchor(rerouteOutput) == new Point(10, 10), "reroute output anchor stays centered");

        var intoReroute = new ConnectionViewModel(sourcePin, rerouteInput);
        var fromReroute = new ConnectionViewModel(rerouteOutput, targetPin);
        var rerouteCenter = new Point(reroute.X + 10, reroute.Y + 10);
        Assert(GetBezierEnd(intoReroute) == rerouteCenter, "connection into reroute ends at route center");
        Assert(GetBezierStart(fromReroute) == rerouteCenter, "connection from reroute starts at route center");
        Assert(RerouteBezierTangentIsHorizontal(intoReroute, useStartTangent: false), "connection into reroute keeps horizontal route tangent");
        Assert(RerouteBezierTangentIsHorizontal(fromReroute, useStartTangent: true), "connection from reroute keeps horizontal route tangent");
        intoReroute.Dispose();
        fromReroute.Dispose();

        CheckSingleRerouteConnectionPath();
        CheckMultiRerouteBacklinkConnectionPath();
    }

    private static void CheckSingleRerouteConnectionPath()
    {
        var editor = new GraphEditorService();
        var source = new PrintLogNodeViewModel("source") { X = 520, Y = 90 };
        var reroute = new RerouteNodeViewModel("reroute", PinKind.Execution) { X = 350, Y = 300 };
        var target = new PrintLogNodeViewModel("target") { X = 40, Y = 90 };
        editor.Nodes.Add(source);
        editor.Nodes.Add(reroute);
        editor.Nodes.Add(target);

        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), reroute.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(reroute.OutputPins.First(pin => pin.Name == "out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        Assert(editor.ConnectionPaths.Count == 1, "single reroute chain renders as one path");
        var geometry = (PathGeometry)editor.ConnectionPaths.Single().PathGeometry;
        Assert(geometry.Figures.Single().Segments.Count == 2, "single reroute path has one segment per ordered span");
        AssertSplineControlsAreClamped(geometry, "single reroute path clamps bezier handles by span distance");
        AssertBezierHandlesFollowSegmentDirection(geometry, "single reroute path keeps bezier handles from folding backward");
        AssertInteriorTangentsContinuous(geometry, "single reroute path keeps continuous tangent through route point");
    }

    private static void CheckMultiRerouteBacklinkConnectionPath()
    {
        var editor = new GraphEditorService();
        var source = new PrintLogNodeViewModel("source") { X = 760, Y = 110 };
        var target = new PrintLogNodeViewModel("target") { X = 40, Y = 110 };
        var routeNearTarget = new RerouteNodeViewModel("route_a", PinKind.Execution) { X = 260, Y = 350 };
        var routeNearSource = new RerouteNodeViewModel("route_b", PinKind.Execution) { X = 610, Y = 310 };
        editor.Nodes.Add(source);
        editor.Nodes.Add(target);
        editor.Nodes.Add(routeNearTarget);
        editor.Nodes.Add(routeNearSource);

        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), routeNearTarget.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeNearTarget.OutputPins.First(pin => pin.Name == "out"), routeNearSource.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeNearSource.OutputPins.First(pin => pin.Name == "out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        Assert(editor.ConnectionPaths.Count == 1, "multi reroute backlink renders as one path");
        var geometry = (PathGeometry)editor.ConnectionPaths.Single().PathGeometry;
        var figure = geometry.Figures.Single();
        var firstSegment = (BezierSegment)figure.Segments[0];
        AssertPointClose(firstSegment.Point3, RerouteCenter(routeNearTarget), "reroute path keeps connection-chain route order");
        Assert(figure.Segments.Count == 3, "multi reroute path has one segment per ordered span");
        AssertSplineControlsAreClamped(geometry, "multi reroute backlink clamps bezier handles by span distance");
        AssertBezierHandlesFollowSegmentDirection(geometry, "multi reroute backlink keeps bezier handles from folding backward");
        AssertNearestConnectionFollowsVisibleCurve(editor.ConnectionPaths.Single(), 1, "visible curve hit resolves the middle backing connection");

        routeNearTarget.X = 120;
        routeNearTarget.Y = 500;
        routeNearSource.X = 930;
        routeNearSource.Y = 150;
        FlushDispatcher();

        geometry = (PathGeometry)editor.ConnectionPaths.Single().PathGeometry;
        figure = geometry.Figures.Single();
        var movedFirstSegment = (BezierSegment)figure.Segments[0];
        var movedSecondSegment = (BezierSegment)figure.Segments[1];
        Assert(figure.Segments.Count == 3, "moving reroute nodes keeps segment count stable");
        AssertPointClose(movedFirstSegment.Point3, RerouteCenter(routeNearTarget), "moving reroute nodes does not reorder first route point");
        AssertPointClose(movedSecondSegment.Point3, RerouteCenter(routeNearSource), "moving reroute nodes does not reorder second route point");
        AssertBezierHandlesFollowSegmentDirection(geometry, "moved multi reroute backlink keeps bezier handles from folding backward");
        CheckCompactBackwardRerouteNoFoldback();
    }

    private static void CheckCompactBackwardRerouteNoFoldback()
    {
        var editor = new GraphEditorService();
        var source = new PrintLogNodeViewModel("source") { X = 520, Y = 100 };
        var routeA = new RerouteNodeViewModel("route_a", PinKind.Execution) { X = 300, Y = 250 };
        var routeB = new RerouteNodeViewModel("route_b", PinKind.Execution) { X = 340, Y = 290 };
        var target = new PrintLogNodeViewModel("target") { X = 70, Y = 120 };
        editor.Nodes.Add(source);
        editor.Nodes.Add(routeA);
        editor.Nodes.Add(routeB);
        editor.Nodes.Add(target);

        editor.CreateConnection(source.OutputPins.First(pin => pin.Name == "exec_out"), routeA.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeA.OutputPins.First(pin => pin.Name == "out"), routeB.InputPins.First(pin => pin.Name == "in"));
        editor.CreateConnection(routeB.OutputPins.First(pin => pin.Name == "out"), target.InputPins.First(pin => pin.Name == "exec_in"));

        var geometry = (PathGeometry)editor.ConnectionPaths.Single().PathGeometry;
        AssertSplineControlsAreClamped(geometry, "compact backward reroute path clamps bezier handles by span distance");
        AssertBezierHandlesFollowSegmentDirection(geometry, "compact backward reroute path keeps bezier handles from folding backward");
    }

    private static void CheckExternalRerouteGraphFile()
    {
        string? path = Environment.GetEnvironmentVariable("AUTOMATION_STUDIO_REROUTE_GRAPH_JSON");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        var file = JsonSerializer.Deserialize<GraphFileModel>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("external reroute graph json failed to deserialize");
        var editor = new GraphEditorService();
        editor.LoadFromModel(file);
        Assert(editor.ConnectionPaths.Any(pathView =>
        {
            var geometry = (PathGeometry)pathView.PathGeometry;
            return geometry.Figures.Single().Segments.Count >= 4;
        }), "external reroute graph renders multi-reroute chain as one aggregated path");
    }

    private static Point GetBezierStart(ConnectionViewModel connection)
    {
        var geometry = (PathGeometry)connection.PathGeometry;
        var figure = geometry.Figures.Single();
        return figure.StartPoint;
    }

    private static Point GetBezierEnd(ConnectionViewModel connection)
    {
        var geometry = (PathGeometry)connection.PathGeometry;
        var figure = geometry.Figures.Single();
        var segment = (BezierSegment)figure.Segments.Single();
        return segment.Point3;
    }

    private static bool RerouteBezierTangentIsHorizontal(ConnectionViewModel connection, bool useStartTangent)
    {
        const double epsilon = 0.001;
        var geometry = (PathGeometry)connection.PathGeometry;
        var figure = geometry.Figures.Single();
        var segment = (BezierSegment)figure.Segments.Single();
        return useStartTangent
            ? Math.Abs(segment.Point1.Y - figure.StartPoint.Y) < epsilon
            : Math.Abs(segment.Point2.Y - segment.Point3.Y) < epsilon;
    }

    private static void AssertSplineControlsAreClamped(PathGeometry geometry, string message)
    {
        const double epsilon = 0.001;
        var figure = geometry.Figures.Single();
        Point start = figure.StartPoint;
        foreach (BezierSegment segment in figure.Segments.OfType<BezierSegment>())
        {
            double spanLength = Distance(start, segment.Point3);
            Assert(Distance(start, segment.Point1) <= spanLength * 0.36 + epsilon, message);
            Assert(Distance(segment.Point3, segment.Point2) <= spanLength * 0.36 + epsilon, message);
            start = segment.Point3;
        }
    }

    private static void AssertInteriorTangentsContinuous(PathGeometry geometry, string message)
    {
        const double epsilon = 0.001;
        var segments = geometry.Figures.Single().Segments.OfType<BezierSegment>().ToArray();
        for (int i = 0; i < segments.Length - 1; i++)
        {
            Point routePoint = segments[i].Point3;
            Vector incoming = routePoint - segments[i].Point2;
            Vector outgoing = segments[i + 1].Point1 - routePoint;
            Assert(incoming.Length > epsilon && outgoing.Length > epsilon, message);
            incoming.Normalize();
            outgoing.Normalize();
            Assert(Vector.Multiply(incoming, outgoing) > 0.95, message);
        }
    }

    private static void AssertBezierHandlesFollowSegmentDirection(PathGeometry geometry, string message)
    {
        const double epsilon = -0.001;
        var figure = geometry.Figures.Single();
        Point start = figure.StartPoint;
        foreach (BezierSegment segment in figure.Segments.OfType<BezierSegment>())
        {
            Vector span = segment.Point3 - start;
            Vector outgoingHandle = segment.Point1 - start;
            Vector incomingHandle = segment.Point3 - segment.Point2;
            Assert(Vector.Multiply(outgoingHandle, span) >= epsilon, message);
            Assert(Vector.Multiply(incomingHandle, span) >= epsilon, message);
            start = segment.Point3;
        }
    }

    private static void AssertNearestConnectionFollowsVisibleCurve(ConnectionPathViewModel path, int segmentIndex, string message)
    {
        var geometry = (PathGeometry)path.PathGeometry;
        var figure = geometry.Figures.Single();
        var segments = figure.Segments.OfType<BezierSegment>().ToArray();
        Assert(segmentIndex >= 0 && segmentIndex < segments.Length, "test segment index is valid");

        Point start = segmentIndex == 0 ? figure.StartPoint : segments[segmentIndex - 1].Point3;
        Point sample = Cubic(start, segments[segmentIndex].Point1, segments[segmentIndex].Point2, segments[segmentIndex].Point3, 0.5);
        Assert(ReferenceEquals(path.FindNearestConnection(sample), path.Connections[segmentIndex]), message);
    }

    private static Point Cubic(Point p0, Point p1, Point p2, Point p3, double t)
    {
        double u = 1.0 - t;
        double tt = t * t;
        double uu = u * u;
        double uuu = uu * u;
        double ttt = tt * t;
        return new Point(
            uuu * p0.X + 3.0 * uu * t * p1.X + 3.0 * u * tt * p2.X + ttt * p3.X,
            uuu * p0.Y + 3.0 * uu * t * p1.Y + 3.0 * u * tt * p2.Y + ttt * p3.Y);
    }

    private static Point RerouteCenter(RerouteNodeViewModel reroute) =>
        new(reroute.X + reroute.Width / 2.0, reroute.Y + reroute.Height / 2.0);

    private static void AssertPointClose(Point actual, Point expected, string message)
    {
        Assert(Distance(actual, expected) < 0.001, $"{message}: got ({actual.X:0.###},{actual.Y:0.###}), expected ({expected.X:0.###},{expected.Y:0.###})");
    }

    private static double Distance(Point first, Point second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void FlushDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher;
        dispatcher?.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Render);
    }

    private static void CheckConnectionRebindAfterParameterReorder()
    {
        var editor = new GraphEditorService();
        var entry = new FunctionEntryNodeViewModel("entry");
        var ret = new FunctionReturnNodeViewModel("ret");
        string[] ids = ["float1", "float2", "float3"];
        foreach (var id in ids)
        {
            entry.Parameters.Add(new GraphParameterDefinition { Id = id, Name = id, Type = GraphParameterType.Float });
            ret.Parameters.Add(new GraphParameterDefinition { Id = $"out_{id}", Name = $"out_{id}", Type = GraphParameterType.Float });
        }
        entry.SyncPins();
        ret.SyncPins();
        editor.Nodes.Add(entry);
        editor.Nodes.Add(ret);
        editor.CreateConnection(entry.OutputPins.First(pin => pin.Name == "float1"), ret.InputPins.First(pin => pin.Name == "out_float1"));
        editor.CreateConnection(entry.OutputPins.First(pin => pin.Name == "float2"), ret.InputPins.First(pin => pin.Name == "out_float2"));
        editor.CreateConnection(entry.OutputPins.First(pin => pin.Name == "float3"), ret.InputPins.First(pin => pin.Name == "out_float3"));
        var oldFloat1Pin = entry.OutputPins.First(pin => pin.Name == "float1");

        entry.MoveParameter(entry.Parameters.First(parameter => parameter.Id == "float2"), -1);
        ret.MoveParameter(ret.Parameters.First(parameter => parameter.Id == "out_float2"), -1);
        editor.RebindConnectionsToCurrentPins();

        Assert(editor.Connections.Count == 3, "parameter reorder keeps all connections");
        Assert(editor.Connections.All(connection => entry.OutputPins.Contains(connection.SourcePin) && ret.InputPins.Contains(connection.TargetPin)),
            "parameter reorder rebinds connections to current pin objects");
        Assert(!editor.Connections.Any(connection => ReferenceEquals(connection.SourcePin, oldFloat1Pin)),
            "parameter reorder does not keep old pin objects");
        Assert(editor.Connections.Any(connection => connection.SourcePin.Name == "float2" && connection.TargetPin.Name == "out_float2"),
            "parameter reorder keeps connections by stable parameter ids");
    }

    private static void CheckDetailsPanelText(MainWindow window)
    {
        var title = window.FindName("DetailsPanelTitleTextBlock") as TextBlock;
        Assert(title?.Text == "细节面板", "details panel title uses new text");
    }

    private static void CheckLogRichTextBoxCopy(MainWindow window)
    {
        Logger.Entries.Clear();
        Logger.Info("copy-line-one");
        Logger.Warn("copy-line-two");
        Invoke(window, "RefreshLogList");
        FlushDispatcher();

        var logBox = Get<RichTextBox>(window, "LogRichTextBox");
        logBox.Focus();
        FlushDispatcher();
        Clipboard.SetText("before-copy");
        ApplicationCommands.SelectAll.Execute(null, logBox);
        ApplicationCommands.Copy.Execute(null, logBox);
        string copied = Clipboard.GetText();

        Assert(copied.Contains("copy-line-one", StringComparison.Ordinal) &&
               copied.Contains("copy-line-two", StringComparison.Ordinal),
            "log rich text box supports Ctrl+A/C copy across multiple lines");
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

        Invoke(window, "OpenContentAsset", script);
        var searchableFunctions = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetCallableFunctions")!).Select(item => item.Name).ToArray();
        Assert(searchableFunctions.Contains("LocalFn"), "script-local function remains searchable");
        Assert(searchableFunctions.Contains("FnLib/PublicFn"), "public library function is searchable");
        Assert(!searchableFunctions.Contains("FnLib/HiddenFn"), "hidden library function is not searchable");

        var runtimeFunctions = ((IEnumerable<CallableGraphItem>)Invoke(window, "GetRuntimeCallableFunctions")!).Select(item => item.Name).ToArray();
        Assert(!runtimeFunctions.Contains("FnLib/HiddenFn"), "runtime library hides private functions from other scripts");

        Invoke(window, "OpenContentAsset", functionLibrary);
        Assert(window.FunctionListItems.All(item => item.ShowLibraryPublishOption), "function library rows show publish checkbox");
        Invoke(window, "OpenContentAsset", script);
        Assert(window.FunctionListItems.All(item => !item.ShowLibraryPublishOption), "script function rows hide publish checkbox");

        var service = new GraphLibraryService();
        service.SaveContentLibrary([functionLibrary], functionLibrary.Id);
        var reloaded = service.LoadContentLibrary();
        Assert(reloaded.First(item => item.Kind == ContentAssetKind.FunctionLibrary).Functions.Single(item => item.Name == "PublicFn").IsPublicToLibrary, "function publish flag persists");
    }

    private static void CheckSaveAllClearsNestedDirty(MainWindow window)
    {
        ResetContent(window);
        var script = window.ContentBrowserItems.First(item => item.Kind == ContentAssetKind.Script);
        script.EventGraphs.Add(new GraphListItemViewModel { Name = "NestedEvent", IsDirty = true });
        script.Functions.Add(new GraphListItemViewModel { Name = "NestedFunction", Kind = GraphAssetKind.Function, IsDirty = true });
        script.IsDirty = true;

        Invoke(window, "SaveAllAssets");
        Assert(!script.IsDirty, "save all clears asset dirty");
        Assert(script.EventGraphs.Concat(script.Functions).All(item => !item.IsDirty), "save all clears nested graph dirty");
    }

    private static GraphRuntimeConnection Exec(string sourceNodeId, string sourcePinName, string targetNodeId, string targetPinName) =>
        new(sourceNodeId, sourcePinName, PinKind.Execution, targetNodeId, targetPinName, PinKind.Execution);

    private static GraphRuntimeConnection StringConn(string sourceNodeId, string sourcePinName, string targetNodeId, string targetPinName) =>
        new(sourceNodeId, sourcePinName, PinKind.String, targetNodeId, targetPinName, PinKind.String);

    private static ConnectionFileModel FileConn(string sourceNodeId, string sourcePinName, string targetNodeId, string targetPinName) => new()
    {
        SourceNodeId = sourceNodeId,
        SourcePinName = sourcePinName,
        TargetNodeId = targetNodeId,
        TargetPinName = targetPinName,
    };

    private static object? Invoke(object target, string name, params object?[] args)
    {
        var method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(target.GetType().Name, name);
        return method.Invoke(target, args);
    }

    private static object? InvokeStatic(Type type, string name, params object?[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.Name, name);
        return method.Invoke(null, args);
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
