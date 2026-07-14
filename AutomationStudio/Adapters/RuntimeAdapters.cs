namespace AutomationStudioWpf.Adapters;

using AutomationStudioWpf.Services;

public sealed class RuntimeAdapters
{
    public RuntimeAdapters()
        : this(PythonEnvironmentService.Shared)
    {
    }

    internal RuntimeAdapters(PythonEnvironmentService pythonEnvironment)
    {
        Mouse = new Win32MouseAdapter();
        Keyboard = new Win32KeyboardAdapter();
        Window = new Win32WindowAdapter();
        Process = new ProcessAdapter();
        Python = new PythonScriptAdapter(pythonEnvironment);
        Screenshot = new ScreenshotAdapter();
    }

    public RuntimeAdapters(
        IMouseAdapter mouse,
        IKeyboardAdapter keyboard,
        IWindowAdapter window,
        IProcessAdapter process,
        IPythonScriptAdapter python,
        IScreenshotAdapter screenshot)
    {
        Mouse = mouse;
        Keyboard = keyboard;
        Window = window;
        Process = process;
        Python = python;
        Screenshot = screenshot;
    }

    public IMouseAdapter Mouse { get; }

    public IKeyboardAdapter Keyboard { get; }

    public IWindowAdapter Window { get; }

    public IProcessAdapter Process { get; }

    public IPythonScriptAdapter Python { get; }

    public IScreenshotAdapter Screenshot { get; }

    internal IDisposable BeginExecutionInputScope()
    {
        RuntimeShutdownGate.ThrowIfShutdownStarted();
        Guid executionId = Guid.NewGuid();
        IDisposable ambientScope = RuntimeExecutionInputContext.Enter(executionId);
        return new ExecutionInputScope(executionId, ambientScope, Keyboard, Mouse);
    }

    internal void ReleaseAllInputs()
    {
        if (Keyboard is IExecutionScopedInputAdapter scopedKeyboard)
            scopedKeyboard.ReleaseAllExecutions();
        else
            Keyboard.ReleaseAllKeys();

        if (Mouse is IExecutionScopedInputAdapter scopedMouse)
            scopedMouse.ReleaseAllExecutions();
    }

    private sealed class ExecutionInputScope(
        Guid executionId,
        IDisposable ambientScope,
        IKeyboardAdapter keyboard,
        IMouseAdapter mouse) : IDisposable
    {
        private IDisposable? _ambientScope = ambientScope;

        public void Dispose()
        {
            IDisposable? scope = Interlocked.Exchange(ref _ambientScope, null);
            if (scope is null)
                return;

            try
            {
                if (keyboard is IExecutionScopedInputAdapter scopedKeyboard)
                    scopedKeyboard.ReleaseExecution(executionId);
                if (mouse is IExecutionScopedInputAdapter scopedMouse)
                    scopedMouse.ReleaseExecution(executionId);
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
