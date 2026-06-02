namespace AutomationStudioWpf.Adapters;

public sealed class RuntimeAdapters
{
    public RuntimeAdapters()
        : this(
            new Win32MouseAdapter(),
            new Win32KeyboardAdapter(),
            new Win32WindowAdapter(),
            new ProcessAdapter(),
            new PythonScriptAdapter())
    {
    }

    public RuntimeAdapters(
        IMouseAdapter mouse,
        IKeyboardAdapter keyboard,
        IWindowAdapter window,
        IProcessAdapter process,
        IPythonScriptAdapter python)
    {
        Mouse = mouse;
        Keyboard = keyboard;
        Window = window;
        Process = process;
        Python = python;
    }

    public IMouseAdapter Mouse { get; }

    public IKeyboardAdapter Keyboard { get; }

    public IWindowAdapter Window { get; }

    public IProcessAdapter Process { get; }

    public IPythonScriptAdapter Python { get; }
}

