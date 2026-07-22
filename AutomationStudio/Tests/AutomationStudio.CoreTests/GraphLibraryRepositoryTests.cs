using System.Text.Json;
using AutomationStudioWpf.Services;

namespace AutomationStudio.CoreTests;

[TestClass]
public sealed class GraphLibraryRepositoryTests
{
    [TestMethod]
    public void SaveAndLoadRoundTripKeepsLibraryState()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "graph-library.json");
            var repository = new GraphLibraryRepository(path, JsonOptions());
            var state = new GraphLibraryState
            {
                LastSelectedContentId = "asset-1",
                ContentAssets =
                [
                    new ContentAssetModel
                    {
                        Id = "asset-1",
                        Name = "Script A",
                        Kind = ContentAssetKind.Script,
                    },
                ],
            };

            repository.Save(state);
            GraphLibraryState loaded = repository.Load();

            Assert.AreEqual("asset-1", loaded.LastSelectedContentId);
            Assert.AreEqual("Script A", loaded.ContentAssets.Single().Name);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [TestMethod]
    public void LoadRecoversBackupAndRepairsPrimaryFile()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "graph-library.json");
            var repository = new GraphLibraryRepository(path, JsonOptions());
            repository.Save(new GraphLibraryState
            {
                LastSelectedContentId = "first",
            });
            repository.Save(new GraphLibraryState
            {
                LastSelectedContentId = "second",
            });

            File.WriteAllText(path, "{ invalid json");

            GraphLibraryState recovered = repository.Load();

            Assert.AreEqual("first", recovered.LastSelectedContentId);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(File.ReadAllText(path).Contains("first", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
    };

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AutomationStudio.CoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
        }
    }
}
