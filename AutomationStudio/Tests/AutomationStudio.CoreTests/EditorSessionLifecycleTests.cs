using AutomationStudioWpf.Interaction;
using AutomationStudioWpf.Services;
using System.Windows;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class EditorSessionLifecycleTests
{
    [STATestMethod]
    public void DisposeDetachesSurfaceAndInvalidatesContext()
    {
        EnsureApplicationResources();
        var asset = new ContentAssetViewModel
        {
            Name = "LifecycleScript",
            Kind = ContentAssetKind.Script,
        };
        var session = new EditorSessionViewModel(asset);
        EditorSurfaceContext context = session.SurfaceContext!;
        var surface = session.Surface!;
        surface.Attach(session, context);

        session.Dispose();
        session.Dispose();

        Assert.IsTrue(context.IsDisposed);
        Assert.IsNull(surface.Session);
        Assert.IsNull(surface.SurfaceContext);
        Assert.IsNull(surface.DataContext);
        Assert.ThrowsExactly<ObjectDisposedException>(() => session.EnsureSurfaceContext());
        Assert.ThrowsExactly<ObjectDisposedException>(() => context.RebuildControllers());
    }

    private static void EnsureApplicationResources()
    {
        if (Application.Current is not null)
            return;

        var app = new AutomationStudioWpf.App();
        app.InitializeComponent();
    }
}
