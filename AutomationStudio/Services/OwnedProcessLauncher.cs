using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AutomationStudioWpf.Services;

internal sealed class OwnedProcessHandle : IDisposable
{
    private readonly StreamReader? _standardOutput;
    private readonly StreamReader? _standardError;
    private IDisposable? _registration;
    private bool _disposed;

    internal OwnedProcessHandle(
        Process process,
        StreamReader? standardOutput,
        StreamReader? standardError,
        IntPtr primaryThreadHandle)
    {
        Process = process;
        _standardOutput = standardOutput;
        _standardError = standardError;
        PrimaryThreadHandle = primaryThreadHandle;
    }

    public Process Process { get; }

    public StreamReader? StandardOutput => _standardOutput;

    public StreamReader? StandardError => _standardError;

    private IntPtr PrimaryThreadHandle { get; }

    internal void Resume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint result = ResumeThread(PrimaryThreadHandle);
        if (result == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复子进程主线程。");
    }

    internal void AttachRegistration(IDisposable registration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _registration = registration;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _registration?.Dispose();
        _registration = null;
        _standardOutput?.Dispose();
        _standardError?.Dispose();
        Process.Dispose();
        CloseHandle(PrimaryThreadHandle);
    }

    internal static OwnedProcessHandle Launch(ProcessStartInfo startInfo)
    {
        if (startInfo.UseShellExecute)
            throw new InvalidOperationException("OwnedProcessLauncher 仅支持 UseShellExecute=false。");

        if (string.IsNullOrWhiteSpace(startInfo.FileName))
            throw new ArgumentException("子进程文件名为空。", nameof(startInfo));

        using var stdin = CreateNullHandle();
        SafeFileHandle? stdoutRead = null;
        SafeFileHandle? stdoutWrite = null;
        SafeFileHandle? stderrRead = null;
        SafeFileHandle? stderrWrite = null;
        IntPtr primaryThread = IntPtr.Zero;
        PROCESS_INFORMATION processInfo = default;
        bool processCreated = false;
        bool primaryThreadTransferred = false;

        try
        {
            if (startInfo.RedirectStandardOutput)
                CreatePipePair(out stdoutRead, out stdoutWrite);
            if (startInfo.RedirectStandardError)
                CreatePipePair(out stderrRead, out stderrWrite);

            if (stdoutRead is not null)
                SetNonInheritable(stdoutRead);
            if (stderrRead is not null)
                SetNonInheritable(stderrRead);

            var startupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFO>(),
                dwFlags = STARTF_USESTDHANDLES,
                hStdInput = stdin.DangerousGetHandle(),
                hStdOutput = (stdoutWrite ??= CreateNullHandle()).DangerousGetHandle(),
                hStdError = (stderrWrite ??= CreateNullHandle()).DangerousGetHandle(),
            };

            string commandLine = BuildCommandLine(startInfo);
            var commandLineBuffer = new StringBuilder(commandLine);
            string environmentBlock = BuildEnvironmentBlock(startInfo.Environment);
            uint creationFlags = CREATE_SUSPENDED | CREATE_UNICODE_ENVIRONMENT;
            if (startInfo.CreateNoWindow)
                creationFlags |= CREATE_NO_WINDOW;

            bool created = CreateProcessW(
                applicationName: null,
                commandLineBuffer,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: true,
                creationFlags,
                environmentBlock,
                string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
                    ? AppContext.BaseDirectory
                    : startInfo.WorkingDirectory,
                ref startupInfo,
                out processInfo);
            if (!created)
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"无法启动子进程：{startInfo.FileName}");

            processCreated = true;
            primaryThread = processInfo.hThread;
            processInfo.hThread = IntPtr.Zero;
            CloseHandle(processInfo.hProcess);
            processInfo.hProcess = IntPtr.Zero;

            stdoutWrite?.Dispose();
            stdoutWrite = null;
            stderrWrite?.Dispose();
            stderrWrite = null;

            var process = Process.GetProcessById((int)processInfo.dwProcessId);
            var output = stdoutRead is null ? null : CreateReader(stdoutRead);
            stdoutRead = null;
            var error = stderrRead is null ? null : CreateReader(stderrRead);
            stderrRead = null;
            var owned = new OwnedProcessHandle(process, output, error, primaryThread);
            primaryThreadTransferred = true;
            return owned;
        }
        catch
        {
            if (processCreated && processInfo.dwProcessId != 0)
            {
                try
                {
                    using Process process = Process.GetProcessById((int)processInfo.dwProcessId);
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                catch
                {
                }
            }

            throw;
        }
        finally
        {
            if (processInfo.hThread != IntPtr.Zero)
                CloseHandle(processInfo.hThread);
            if (processInfo.hProcess != IntPtr.Zero)
                CloseHandle(processInfo.hProcess);
            if (primaryThread != IntPtr.Zero && !primaryThreadTransferred)
                CloseHandle(primaryThread);
            stdoutRead?.Dispose();
            stdoutWrite?.Dispose();
            stderrRead?.Dispose();
            stderrWrite?.Dispose();
        }
    }

    private static StreamReader CreateReader(SafeFileHandle handle) =>
        new(new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: true), new UTF8Encoding(false), true);

    private static void CreatePipePair(out SafeFileHandle read, out SafeFileHandle write)
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1,
        };
        if (!CreatePipe(out read, out write, ref securityAttributes, 64 * 1024))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建子进程输出管道。");
    }

    private static void SetNonInheritable(SafeFileHandle handle)
    {
        if (!SetHandleInformation(handle, HANDLE_FLAG_INHERIT, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置子进程输出管道属性。");
    }

    private static SafeFileHandle CreateNullHandle()
    {
        var securityAttributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1,
        };
        SafeFileHandle handle = CreateFileW(
            "NUL",
            GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            ref securityAttributes,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开 NUL 设备。");
        return handle;
    }

    private static string BuildCommandLine(ProcessStartInfo startInfo)
    {
        var parts = new List<string> { QuoteArgument(startInfo.FileName) };
        parts.AddRange(startInfo.ArgumentList.Select(QuoteArgument));
        return string.Join(' ', parts);
    }

    private static string BuildEnvironmentBlock(IDictionary<string, string?> environment)
    {
        var entries = environment
            .Where(pair => pair.Key.Length > 0 && pair.Value is not null)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return string.Join('\0', entries) + "\0\0";
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length == 0)
            return "\"\"";
        if (!value.Any(char.IsWhiteSpace) && !value.Contains('"'))
            return value;

        var builder = new StringBuilder(value.Length + 2).Append('"');
        int slashCount = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashCount++;
                continue;
            }

            if (character == '"')
                builder.Append('\\', slashCount * 2 + 1);
            else
                builder.Append('\\', slashCount);
            slashCount = 0;
            builder.Append(character);
        }

        builder.Append('\\', slashCount * 2).Append('"');
        return builder.ToString();
    }

    private const uint CREATE_SUSPENDED = 0x00000004;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_NO_WINDOW = 0x08000000;
    private const uint STARTF_USESTDHANDLES = 0x00000100;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        string environment,
        string currentDirectory,
        ref STARTUPINFO startupInfo,
        out PROCESS_INFORMATION processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle readPipe,
        out SafeFileHandle writePipe,
        ref SECURITY_ATTRIBUTES pipeAttributes,
        int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        ref SECURITY_ATTRIBUTES securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr threadHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
