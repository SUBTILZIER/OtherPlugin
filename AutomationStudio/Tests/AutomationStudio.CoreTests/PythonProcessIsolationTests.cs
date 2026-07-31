using System.Diagnostics;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class PythonProcessIsolationTests
{
    [TestMethod]
    public void PythonStartInfoDisablesBytecodeWritesEvenInIsolatedMode()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = @"C:\runtime\python.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-I");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("import cv2");
        startInfo.Environment["PYTHONPATH"] = @"C:\unsafe";
        startInfo.Environment["PYTHONHOME"] = @"C:\unsafe";

        PythonEnvironmentService.ConfigureIsolatedPythonStartInfo(startInfo);

        CollectionAssert.Contains(startInfo.ArgumentList.ToArray(), "-B");
        Assert.AreEqual("-B", startInfo.ArgumentList[0]);
        Assert.AreEqual("1", startInfo.Environment["PYTHONDONTWRITEBYTECODE"]);
        Assert.AreEqual("1", startInfo.Environment["PYTHONNOUSERSITE"]);
        Assert.AreEqual("1", startInfo.Environment["PYTHONUTF8"]);
        Assert.IsFalse(startInfo.Environment.ContainsKey("PYTHONPATH"));
        Assert.IsFalse(startInfo.Environment.ContainsKey("PYTHONHOME"));
    }

    [TestMethod]
    public void PythonStartInfoDoesNotDuplicateBytecodeSwitch()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python.exe",
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-B");

        PythonEnvironmentService.ConfigureIsolatedPythonStartInfo(startInfo);
        PythonEnvironmentService.ConfigureIsolatedPythonStartInfo(startInfo);

        Assert.AreEqual(1, startInfo.ArgumentList.Count(argument => argument == "-B"));
    }

    [TestMethod]
    public void NonPythonStartInfoIsUnchanged()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "where.exe",
            UseShellExecute = false,
        };

        PythonEnvironmentService.ConfigureIsolatedPythonStartInfo(startInfo);

        Assert.AreEqual(0, startInfo.ArgumentList.Count);
        Assert.IsFalse(startInfo.Environment.ContainsKey("PYTHONDONTWRITEBYTECODE"));
    }
}
