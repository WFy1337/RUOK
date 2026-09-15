using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;

namespace RUOK_App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var instance = AppInstance.FindOrRegisterForKey(AppRuntime.InstanceKey);
        if (!instance.IsCurrent)
        {
            AllowSetForegroundWindow(instance.ProcessId);
            var arguments = AppInstance.GetCurrent().GetActivatedEventArgs();
            using var completed = new EventWaitHandle(false, EventResetMode.ManualReset);
            var redirect = Task.Run(async () =>
            {
                try
                {
                    await instance.RedirectActivationToAsync(arguments);
                }
                finally
                {
                    completed.Set();
                }
            });
            // Pump COM/window messages while the MTA performs redirection; do not block an STA on an async RPC.
            var result = CoWaitForMultipleHandles(0x8 | 0x10, uint.MaxValue, 1,
                [completed.SafeWaitHandle.DangerousGetHandle()], out _);
            Marshal.ThrowExceptionForHR(result);
            redirect.GetAwaiter().GetResult();
            return 0;
        }
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(instance);
        });
        return 0;
    }

    [DllImport("ole32.dll")]
    private static extern int CoWaitForMultipleHandles(uint flags, uint milliseconds, uint count,
        [In] nint[] handles, out uint index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
