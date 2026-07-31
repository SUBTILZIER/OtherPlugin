using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class ReleaseTestOptionsTests
{
    [TestMethod]
    public void SelfTestRequiresRootAndReport()
    {
        bool parsed = ReleaseTestOptions.TryParse(
            ["--release-self-test"],
            out ReleaseTestOptions options,
            out string error);

        Assert.IsFalse(parsed);
        Assert.IsTrue(options.IsSelfTest);
        StringAssert.Contains(error, "--release-test-root");
        StringAssert.Contains(error, "--report");
    }

    [TestMethod]
    public void SelfTestParsesSeparateAndInlineValues()
    {
        bool parsed = ReleaseTestOptions.TryParse(
            ["--release-self-test", "--release-test-root=C:\\test-root", "--report", "C:\\test-root\\report.json"],
            out ReleaseTestOptions options,
            out string error);

        Assert.IsTrue(parsed, error);
        Assert.IsTrue(options.IsSelfTest);
        Assert.AreEqual("C:\\test-root", options.TestRoot);
        Assert.AreEqual("C:\\test-root\\report.json", options.ReportPath);
    }

    [TestMethod]
    public void IsolatedNormalLaunchMayReceiveOnlyTestRoot()
    {
        bool parsed = ReleaseTestOptions.TryParse(
            ["--release-test-root", "C:\\test-root"],
            out ReleaseTestOptions options,
            out string error);

        Assert.IsTrue(parsed, error);
        Assert.IsFalse(options.IsSelfTest);
        Assert.AreEqual("C:\\test-root", options.TestRoot);
        Assert.IsNull(options.ReportPath);
    }

    [TestMethod]
    public void ReportWithoutSelfTestIsRejected()
    {
        bool parsed = ReleaseTestOptions.TryParse(
            ["--report", "C:\\report.json"],
            out _,
            out string error);

        Assert.IsFalse(parsed);
        StringAssert.Contains(error, "--release-self-test");
    }

    [TestMethod]
    public void SafeRootRequiresAllowedChildAndMarker()
    {
        string root = CreateMarkedRoot();
        try
        {
            Assert.AreEqual(Path.GetFullPath(root), ReleaseTestEnvironment.ValidateRoot(root));

            string unmarked = Path.Combine(ReleaseTestEnvironment.AllowedRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unmarked);
            try
            {
                Assert.ThrowsExactly<InvalidOperationException>(() =>
                    ReleaseTestEnvironment.ValidateRoot(unmarked));
            }
            finally
            {
                Directory.Delete(unmarked, recursive: true);
            }

            string outside = Path.Combine(Path.GetTempPath(), "AutomationStudio.Unsafe", Guid.NewGuid().ToString("N"));
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ReleaseTestEnvironment.ValidateRoot(outside));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ReportMustStayInsideMarkedRoot()
    {
        string root = CreateMarkedRoot();
        try
        {
            string report = Path.Combine(root, "reports", "self-test.json");
            Assert.AreEqual(Path.GetFullPath(report), ReleaseTestEnvironment.ValidateReportPath(report, root));

            string outside = Path.Combine(Path.GetTempPath(), "outside-report.json");
            Assert.ThrowsExactly<InvalidOperationException>(() =>
                ReleaseTestEnvironment.ValidateReportPath(outside, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateMarkedRoot()
    {
        string root = Path.Combine(ReleaseTestEnvironment.AllowedRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(
            Path.Combine(root, ReleaseTestEnvironment.MarkerFileName),
            ReleaseTestEnvironment.MarkerContent);
        return root;
    }
}
