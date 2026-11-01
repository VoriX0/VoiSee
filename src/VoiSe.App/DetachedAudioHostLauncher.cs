using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiSe.App;

/// <summary>
/// Starts VoiSe.AudioHost.exe with Explorer as its explicit parent so application
/// screen capture of the VoiSee UI process tree cannot automatically include it.
/// </summary>
internal static class DetachedAudioHostLauncher
{
    private const uint ProcessCreateProcess = 0x0080;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private static readonly IntPtr ProcThreadAttributeParentProcess = new(0x00020000);

    public static Process StartWithExplorerParent(string executablePath, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        using var explorer = FindExplorerForCurrentSession()
            ?? throw new InvalidOperationException(UiText.L("Windows_Explorer_is_not_running_in_the_current_b0a2523f"));

        var parentHandle = OpenProcess(
            ProcessCreateProcess | ProcessQueryLimitedInformation,
            inheritHandle: false,
            explorer.Id);
        if (parentHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                UiText.L("Could_not_open_Windows_Explorer_as_the_Audio_H_ec904e4a"));
        }

        IntPtr attributeList = IntPtr.Zero;
        IntPtr parentHandleValue = IntPtr.Zero;
        try
        {
            var attributeListSize = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);

            attributeList = Marshal.AllocHGlobal(attributeListSize);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    UiText.L("Could_not_initialize_the_Audio_Host_process_at_434acbfe"));
            }

            parentHandleValue = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(parentHandleValue, parentHandle);

            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeParentProcess,
                    parentHandleValue,
                    new IntPtr(IntPtr.Size),
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    UiText.L("Could_not_assign_Windows_Explorer_as_the_Audio_22ff8587"));
            }

            var startupInfo = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    Size = Marshal.SizeOf<StartupInfoEx>()
                },
                AttributeList = attributeList
            };

            var commandLine = new StringBuilder();
            commandLine.Append('"').Append(executablePath).Append('"');
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                commandLine.Append(' ').Append(arguments);
            }

            var created = CreateProcess(
                executablePath,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                inheritHandles: false,
                ExtendedStartupInfoPresent | CreateNoWindow,
                IntPtr.Zero,
                Path.GetDirectoryName(executablePath),
                ref startupInfo,
                out var processInformation);

            if (!created)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    UiText.L("Could_not_start_VoiSe_AudioHost_exe_6df17e9e"));
            }

            try
            {
                return Process.GetProcessById(unchecked((int)processInformation.ProcessId));
            }
            finally
            {
                CloseHandle(processInformation.ThreadHandle);
                CloseHandle(processInformation.ProcessHandle);
            }
        }
        finally
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (parentHandleValue != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(parentHandleValue);
            }

            CloseHandle(parentHandle);
        }
    }

    private static Process? FindExplorerForCurrentSession()
    {
        var currentSessionId = Process.GetCurrentProcess().SessionId;
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (process.SessionId == currentSessionId)
                {
                    return process;
                }
            }
            catch
            {
                process.Dispose();
                continue;
            }

            process.Dispose();
        }

        return null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        int flags,
        ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        IntPtr size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ProcessHandle;
        public IntPtr ThreadHandle;
        public uint ProcessId;
        public uint ThreadId;
    }
}
