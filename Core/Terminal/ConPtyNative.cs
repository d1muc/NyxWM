using System;
using System.Runtime.InteropServices;
using System.Text;

namespace nyxWM
{
    /// <summary>
    /// Raw P/Invoke surface for the Windows pseudoconsole (ConPTY) API and the
    /// process-creation helpers it requires. Everything here is kernel32 only,
    /// so the terminal backend works on Windows 10 1809 and newer without any
    /// third-party dependency.
    /// </summary>
    internal static class ConPtyNative
    {
        internal const uint ExtendedStartupInfoPresent = 0x00080000;
        internal const uint CreateUnicodeEnvironment = 0x00000400;
        internal const uint HandleFlagInherit = 0x00000001;
        internal const uint WaitObject0 = 0x00000000;
        internal const uint WaitTimeout = 0x00000102;

        /// <summary>PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE — 0x00020016.</summary>
        internal static readonly IntPtr ProcThreadAttributePseudoConsole = new(0x00020016);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Coord
        {
            internal short X;
            internal short Y;

            internal Coord(short x, short y)
            {
                X = x;
                Y = y;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            internal int cb;
            internal string? lpReserved;
            internal string? lpDesktop;
            internal string? lpTitle;
            internal int dwX;
            internal int dwY;
            internal int dwXSize;
            internal int dwYSize;
            internal int dwXCountChars;
            internal int dwYCountChars;
            internal int dwFillAttribute;
            internal int dwFlags;
            internal short wShowWindow;
            internal short cbReserved2;
            internal IntPtr lpReserved2;
            internal IntPtr hStdInput;
            internal IntPtr hStdOutput;
            internal IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfoEx
        {
            internal StartupInfo StartupInfo;
            internal IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            internal IntPtr hProcess;
            internal IntPtr hThread;
            internal int dwProcessId;
            internal int dwThreadId;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreatePipe(
            out IntPtr hReadPipe,
            out IntPtr hWritePipe,
            IntPtr lpPipeAttributes,
            uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetHandleInformation(IntPtr hObject, uint dwMask, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int CreatePseudoConsole(
            Coord size,
            IntPtr hInput,
            IntPtr hOutput,
            uint dwFlags,
            out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool InitializeProcThreadAttributeList(
            IntPtr lpAttributeList,
            int dwAttributeCount,
            int dwFlags,
            ref IntPtr lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateProcThreadAttribute(
            IntPtr lpAttributeList,
            uint dwFlags,
            IntPtr attribute,
            IntPtr lpValue,
            IntPtr cbSize,
            IntPtr lpPreviousValue,
            IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string? lpApplicationName,
            StringBuilder lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            ref StartupInfoEx lpStartupInfo,
            out ProcessInformation lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

        /// <summary>
        /// Throws a <see cref="TerminalStartupException"/> when an HRESULT-based
        /// ConPTY call fails.
        /// </summary>
        internal static void ThrowIfFailed(int hresult, string operation)
        {
            if (hresult < 0)
                throw new TerminalStartupException($"{operation} failed (HRESULT 0x{hresult:X8}).");
        }

        /// <summary>
        /// Throws a <see cref="TerminalStartupException"/> built from the last
        /// Win32 error, which is what most of these calls report.
        /// </summary>
        internal static void ThrowWin32(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            throw new TerminalStartupException(
                $"{operation} failed (Win32 error {error}: {new System.ComponentModel.Win32Exception(error).Message}).");
        }
    }
}
