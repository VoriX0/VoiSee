using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Linq;
using System.Threading;
using WinRT;
using VoiSe.Audio;

namespace VoiSe.App;

public static class Program
{
    internal static bool StartInBackground { get; private set; }
    internal static SingleInstanceCoordinator? InstanceCoordinator { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (VirtualMicOutputHost.TryRun(args, out var hostExitCode))
        {
            Environment.ExitCode = hostExitCode;
            return;
        }

        try
        {
            StartupLog.Write("Program.Main started.");
            StartInBackground = args.Any(argument =>
                string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));

            InstanceCoordinator = SingleInstanceCoordinator.Create();
            if (!InstanceCoordinator.IsPrimary)
            {
                var signaled = InstanceCoordinator.SignalPrimaryInstanceAsync().GetAwaiter().GetResult();
                StartupLog.Write(signaled
                    ? "Existing VoiSee instance was activated; secondary process exits."
                    : "Existing VoiSee instance was detected, but activation signaling failed.");
                InstanceCoordinator.Dispose();
                InstanceCoordinator = null;
                return;
            }

            ComWrappersSupport.InitializeComWrappers();
            StartupLog.Write("WinRT COM wrappers initialized.");

            Microsoft.UI.Xaml.Application.Start(_initializationCallbackParams =>
            {
                try
                {
                    StartupLog.Write("Application.Start callback entered.");
                    var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                    SynchronizationContext.SetSynchronizationContext(context);
                    new App();
                    StartupLog.Write("App instance created from Program.Main.");
                }
                catch (Exception ex)
                {
                    StartupLog.Write("Application.Start callback error: " + ex);
                    throw;
                }
            });

            StartupLog.Write("Program.Main finished normally.");
        }
        catch (Exception ex)
        {
            StartupLog.Write("Program.Main fatal error: " + ex);
            Environment.ExitCode = 1;
        }
        finally
        {
            InstanceCoordinator?.Dispose();
            InstanceCoordinator = null;
        }
    }
}
