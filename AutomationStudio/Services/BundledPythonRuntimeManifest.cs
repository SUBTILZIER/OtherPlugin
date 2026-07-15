using System.IO;
using System.Text.Json;

namespace AutomationStudioWpf.Services;

internal sealed class BundledPythonRuntimeManifest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public int SchemaVersion { get; set; }

    public string PythonVersion { get; set; } = string.Empty;

    public string Architecture { get; set; } = string.Empty;

    public Dictionary<string, string> Packages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static bool TryLoad(string path, out BundledPythonRuntimeManifest manifest, out string error)
    {
        manifest = new BundledPythonRuntimeManifest();
        error = string.Empty;
        try
        {
            if (!File.Exists(path))
            {
                error = $"缺少运行时清单：{path}";
                return false;
            }

            manifest = JsonSerializer.Deserialize<BundledPythonRuntimeManifest>(File.ReadAllText(path), JsonOptions)
                ?? new BundledPythonRuntimeManifest();
            if (manifest.SchemaVersion != 1)
            {
                error = $"不支持的运行时清单版本：{manifest.SchemaVersion}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"运行时清单无法读取：{ex.Message}";
            return false;
        }
    }
}
