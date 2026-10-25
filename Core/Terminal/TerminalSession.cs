using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace nyxWM
{
    /// <summary>
    /// Thrown when the pseudoconsole or the shell process cannot be started.
    /// </summary>
    internal sealed class TerminalStartupException : Exception
    {
        internal TerminalStartupException(string message) : base(message)
        {
        }

        internal TerminalStartupException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Launch settings for <see cref="TerminalSession"/>. Every value has a
    /// working default, so <c>new TerminalSession(new TerminalSessionOptions())</c>
    /// starts an interactive MSYS2 zsh with a 120x30 grid.
    /// </summary>
    internal sealed class TerminalSessionOptions
    {
        /// <summary>Root of the MSYS2 installation, e.g. <c>C:\msys64</c>.</summary>
        public string Msys2Root { get; init; } = TerminalEnvironment.DefaultRoot;

        /// <summary>Explicit shell path; null auto-resolves zsh, then bash.</summary>
        public string? ShellPath { get; init; }

        /// <summary>
        /// Command line arguments passed to <see cref="ShellPath"/>. Null uses
        /// the interactive MSYS2 form <c>-l -i</c>; an empty string starts the
        /// shell with no arguments.
        /// </summary>
        public string? ShellArguments { get; init; }

        /// <summary>
        /// Adds the MSYS2 PATH, MSYSTEM and locale variables to the child
        /// environment. False for native Windows shells such as cmd and
        /// PowerShell.
        /// </summary>
        public bool UseMsysEnvironment { get; init; } = true;

        /// <summary>MSYS2 subsystem: UCRT64, CLANG64, MINGW64 or MSYS.</summary>
        public string Msystem { get; init; } = "UCRT64";

        /// <summary>
        /// Windows-side working directory for the shell. Null uses the user
        /// profile. Pass a Windows path (<c>C:\work</c>), not an MSYS path.
        /// </summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>Initial pseudoconsole width in character cells.</summary>
        public int Columns { get; init; } = 120;

        /// <summary>Initial pseudoconsole height in character cells.</summary>
        public int Rows { get; init; } = 30;

        /// <summary>Extra environment variables layered over the inherited ones.</summary>
        public IReadOnlyDictionary<string, string> EnvironmentOverrides { get; init; } =
            new Dictionary<string, string>(0, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Locates the MSYS2 installation and turns it into a launchable shell plus
    /// the environment block the shell expects.
    /// </summary>
    internal static class TerminalEnvironment
    {
        private static readonly string[] CandidateRoots =
        {
            @"C:\msys64",
            @"C:\msys2",
            @"D:\msys64",
            @"D:\msys2"
        };

        /// <summary>First existing MSYS2 root, or <c>C:\msys64</c> as a fallback.</summary>
        internal static string DefaultRoot
        {
            get
            {
                string? fromEnvironment = Environment.GetEnvironmentVariable("MSYS2_ROOT");
                if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
                    return fromEnvironment;

                foreach (string candidate in CandidateRoots)
                {
                    if (File.Exists(Path.Combine(candidate, "usr", "bin", "bash.exe")))
                        return candidate;
                }

                return CandidateRoots[0];
            }
        }

        /// <summary>Resolves zsh when present, otherwise bash, inside the given root.</summary>
        internal static string ResolveShell(string msys2Root)
        {
            if (string.IsNullOrWhiteSpace(msys2Root))
                throw new TerminalStartupException("MSYS2 root is empty.");

            string zsh = Path.Combine(msys2Root, "usr", "bin", "zsh.exe");
            if (File.Exists(zsh))
                return zsh;

            string bash = Path.Combine(msys2Root, "usr", "bin", "bash.exe");
            if (File.Exists(bash))
                return bash;

            throw new TerminalStartupException(
                $"No MSYS2 shell found under '{msys2Root}'. Expected usr\\bin\\zsh.exe or usr\\bin\\bash.exe.");
        }
    }

    /// <summary>
    /// Payload of <see cref="TerminalSession.OutputReceived"/>: the raw bytes
    /// ConPTY produced plus the same bytes decoded as UTF-8. Feed
    /// <see cref="Text"/> straight into <see cref="AnsiScreenBuffer"/>.
    /// </summary>
    internal sealed class TerminalOutputEventArgs : EventArgs
    {
        internal TerminalOutputEventArgs(byte[] data, string text)
        {
            Data = data;
            Text = text;
        }

        /// <summary>Raw pseudoconsole bytes, including ANSI escape sequences.</summary>
        public byte[] Data { get; }

        /// <summary>Incrementally decoded UTF-8 text for the same bytes.</summary>
        public string Text { get; }
    }

    /// <summary>Payload of <see cref="TerminalSession.Faulted"/>.</summary>
    internal sealed class TerminalErrorEventArgs : EventArgs
    {
        internal TerminalErrorEventArgs(Exception error)
        {
            Error = error;
        }

        public Exception Error { get; }
    }

    /// <summary>
    /// Wraps an MSYS2 shell (zsh/bash) running inside a Windows pseudoconsole.
    ///
    /// The shell sees a real TTY, so pacman, git, neofetch, vim and every other
    /// console program behave exactly as they do on Arch: colours, cursor
    /// addressing and full-screen redraws all arrive as genuine ANSI/VT.
    ///
    /// Threading: <see cref="OutputReceived"/>, <see cref="Exited"/> and
    /// <see cref="Faulted"/> are raised on a background reader thread. Marshalling
    /// to the UI thread is the caller's job (WinForms: <c>BeginInvoke</c>).
    /// </summary>
    internal sealed class TerminalSession : IDisposable
    {
        private const int ReadBufferSize = 8192;
        private const int WriteBufferSize = 4096;
        private const int ShutdownTimeoutMs = 2000;
        private const int MaxColumns = 1000;
        private const int MaxRows = 500;

        private readonly TerminalSessionOptions _options;
        private readonly object _sync = new();
        private readonly object _writeLock = new();
        private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();

        private char[] _decodeBuffer = new char[ReadBufferSize * 2];

        private IntPtr _pseudoConsole;
        private IntPtr _inputWrite;
        private IntPtr _outputRead;
        private IntPtr _processHandle;

        private FileStream? _inputStream;
        private FileStream? _outputStream;
        private Thread? _readerThread;

        private int _columns;
        private int _rows;
        private int _started;
        private int _stopping;
        private int _disposed;
        private int _exitRaised;

        internal TerminalSession(TerminalSessionOptions? options = null)
        {
            _options = options ?? new TerminalSessionOptions();
            _columns = ClampDimension(_options.Columns, 1, MaxColumns);
            _rows = ClampDimension(_options.Rows, 1, MaxRows);
        }

        /// <summary>Raised for every chunk the shell writes to its terminal.</summary>
        internal event EventHandler<TerminalOutputEventArgs>? OutputReceived;

        /// <summary>Raised once when the shell process ends.</summary>
        internal event EventHandler? Exited;

        /// <summary>Raised when the reader thread or a resize call fails.</summary>
        internal event EventHandler<TerminalErrorEventArgs>? Faulted;

        /// <summary>Current pseudoconsole width in character cells.</summary>
        public int Columns => Volatile.Read(ref _columns);

        /// <summary>Current pseudoconsole height in character cells.</summary>
        public int Rows => Volatile.Read(ref _rows);

        /// <summary>PID of the shell process, or 0 before <see cref="Start"/>.</summary>
        public int ProcessId { get; private set; }

        /// <summary>True while the shell process is alive.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    if (_processHandle == IntPtr.Zero)
                        return false;

                    return ConPtyNative.WaitForSingleObject(_processHandle, 0) == ConPtyNative.WaitTimeout;
                }
            }
        }

        /// <summary>
        /// Starts the pseudoconsole and the shell. Idempotency is intentionally
        /// not provided: a session is started once, then disposed.
        /// </summary>
        public void Start()
        {
            lock (_sync)
            {
                ThrowIfDisposed();

                if (_started != 0)
                    throw new InvalidOperationException("The terminal session is already running.");

                StartCore();
                Volatile.Write(ref _started, 1);
            }
        }

        /// <summary>Sends text to the shell's stdin, encoded as UTF-8.</summary>
        public void WriteInput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Write(Encoding.UTF8.GetBytes(text));
        }

        /// <summary>Sends a single byte (a literal key code or escape byte).</summary>
        public void WriteByte(byte value)
        {
            Span<byte> buffer = stackalloc byte[1];
            buffer[0] = value;
            Write(buffer);
        }

        /// <summary>Sends raw bytes to the shell's stdin.</summary>
        public void Write(ReadOnlySpan<byte> data)
        {
            if (data.IsEmpty || Volatile.Read(ref _disposed) != 0)
                return;

            lock (_writeLock)
            {
                FileStream? stream = _inputStream;
                if (stream is null)
                    return;

                try
                {
                    stream.Write(data);
                    stream.Flush();
                }
                catch (ObjectDisposedException)
                {
                    // Session torn down between the check and the write.
                }
                catch (IOException)
                {
                    // Shell already exited; the reader thread reports the exit.
                }
            }
        }

        /// <summary>
        /// Resizes the pseudoconsole grid. Safe to call from the UI thread on
        /// every window resize — the shell receives SIGWINCH and full-screen
        /// programs (vim, less, htop) re-layout themselves.
        /// </summary>
        public void Resize(int columns, int rows)
        {
            int width = ClampDimension(columns, 1, MaxColumns);
            int height = ClampDimension(rows, 1, MaxRows);

            Exception? failure = null;

            lock (_sync)
            {
                if (_disposed != 0)
                    return;

                if (_columns == width && _rows == height)
                    return;

                _columns = width;
                _rows = height;

                if (_pseudoConsole == IntPtr.Zero)
                    return;

                int hresult = ConPtyNative.ResizePseudoConsole(
                    _pseudoConsole,
                    new ConPtyNative.Coord((short)width, (short)height));

                if (hresult < 0)
                    failure = new TerminalStartupException(
                        $"ResizePseudoConsole failed (HRESULT 0x{hresult:X8}).");
            }

            if (failure is not null)
                RaiseFaulted(failure);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            Volatile.Write(ref _stopping, 1);

            FileStream? input;
            lock (_writeLock)
            {
                input = _inputStream;
                _inputStream = null;
            }

            try
            {
                input?.Dispose();
            }
            catch (IOException)
            {
                // Broken pipe while shutting down.
            }

            IntPtr pseudoConsole;
            lock (_sync)
            {
                pseudoConsole = _pseudoConsole;
                _pseudoConsole = IntPtr.Zero;
            }

            if (pseudoConsole != IntPtr.Zero)
                ConPtyNative.ClosePseudoConsole(pseudoConsole);

            IntPtr processHandle;
            lock (_sync)
            {
                processHandle = _processHandle;
                _processHandle = IntPtr.Zero;
            }

            if (processHandle != IntPtr.Zero)
            {
                uint wait = ConPtyNative.WaitForSingleObject(processHandle, ShutdownTimeoutMs);
                if (wait == ConPtyNative.WaitTimeout)
                    ConPtyNative.TerminateProcess(processHandle, 0);

                ConPtyNative.CloseHandle(processHandle);
            }

            _readerThread?.Join(ShutdownTimeoutMs);

            FileStream? output;
            lock (_sync)
            {
                output = _outputStream;
                _outputStream = null;
            }

            try
            {
                output?.Dispose();
            }
            catch (IOException)
            {
                // Reader already released the pipe.
            }

            CloseHandle(ref _outputRead);
            CloseHandle(ref _inputWrite);
        }

        private void StartCore()
        {
            string shellPath = _options.ShellPath ?? TerminalEnvironment.ResolveShell(_options.Msys2Root);
            if (!File.Exists(shellPath))
                throw new TerminalStartupException($"Shell not found: {shellPath}");

            var size = new ConPtyNative.Coord((short)_columns, (short)_rows);

            if (!ConPtyNative.CreatePipe(out IntPtr inputRead, out IntPtr inputWrite, IntPtr.Zero, 0))
                ConPtyNative.ThrowWin32("CreatePipe (stdin)");

            if (!ConPtyNative.CreatePipe(out IntPtr outputRead, out IntPtr outputWrite, IntPtr.Zero, 0))
            {
                ConPtyNative.CloseHandle(inputRead);
                ConPtyNative.CloseHandle(inputWrite);
                ConPtyNative.ThrowWin32("CreatePipe (stdout)");
            }

            IntPtr attributeList = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;
            IntPtr pseudoConsole = IntPtr.Zero;
            IntPtr processHandle = IntPtr.Zero;

            try
            {
                ConPtyNative.SetHandleInformation(inputWrite, ConPtyNative.HandleFlagInherit, 0);
                ConPtyNative.SetHandleInformation(outputRead, ConPtyNative.HandleFlagInherit, 0);

                ConPtyNative.ThrowIfFailed(
                    ConPtyNative.CreatePseudoConsole(size, inputRead, outputWrite, 0, out pseudoConsole),
                    "CreatePseudoConsole");

                IntPtr attributeSize = IntPtr.Zero;
                ConPtyNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeSize);
                attributeList = Marshal.AllocHGlobal(attributeSize);

                if (!ConPtyNative.InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeSize))
                    ConPtyNative.ThrowWin32("InitializeProcThreadAttributeList");

                if (!ConPtyNative.UpdateProcThreadAttribute(
                        attributeList,
                        0,
                        ConPtyNative.ProcThreadAttributePseudoConsole,
                        pseudoConsole,
                        (IntPtr)IntPtr.Size,
                        IntPtr.Zero,
                        IntPtr.Zero))
                {
                    ConPtyNative.ThrowWin32("UpdateProcThreadAttribute");
                }

                environmentBlock = Marshal.StringToHGlobalUni(BuildEnvironmentBlock());

                var startupInfo = new ConPtyNative.StartupInfoEx();
                startupInfo.StartupInfo.cb = Marshal.SizeOf<ConPtyNative.StartupInfoEx>();
                startupInfo.lpAttributeList = attributeList;

                string arguments = _options.ShellArguments ?? "-l -i";
                var commandLine = new StringBuilder($"\"{shellPath}\"");
                if (!string.IsNullOrWhiteSpace(arguments))
                    commandLine.Append(' ').Append(arguments);
                string? workingDirectory = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                    : _options.WorkingDirectory;

                if (!ConPtyNative.CreateProcess(
                        shellPath,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        ConPtyNative.ExtendedStartupInfoPresent | ConPtyNative.CreateUnicodeEnvironment,
                        environmentBlock,
                        workingDirectory,
                        ref startupInfo,
                        out ConPtyNative.ProcessInformation processInformation))
                {
                    ConPtyNative.ThrowWin32("CreateProcess");
                }

                processHandle = processInformation.hProcess;
                if (processInformation.hThread != IntPtr.Zero)
                    ConPtyNative.CloseHandle(processInformation.hThread);

                ProcessId = processInformation.dwProcessId;
            }
            catch
            {
                if (processHandle != IntPtr.Zero)
                    ConPtyNative.CloseHandle(processHandle);

                if (pseudoConsole != IntPtr.Zero)
                    ConPtyNative.ClosePseudoConsole(pseudoConsole);

                throw;
            }
            finally
            {
                if (environmentBlock != IntPtr.Zero)
                    Marshal.FreeHGlobal(environmentBlock);

                if (attributeList != IntPtr.Zero)
                {
                    ConPtyNative.DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }

                ConPtyNative.CloseHandle(inputRead);
                ConPtyNative.CloseHandle(outputWrite);
            }

            var inputHandle = new SafeFileHandle(inputWrite, ownsHandle: false);
            var outputHandle = new SafeFileHandle(outputRead, ownsHandle: false);
            var inputStream = new FileStream(inputHandle, FileAccess.Write, WriteBufferSize, isAsync: false);
            var outputStream = new FileStream(outputHandle, FileAccess.Read, ReadBufferSize, isAsync: false);

            _pseudoConsole = pseudoConsole;
            _processHandle = processHandle;
            _inputWrite = inputWrite;
            _outputRead = outputRead;
            _inputStream = inputStream;
            _outputStream = outputStream;

            _readerThread = new Thread(() => ReadLoop(outputStream))
            {
                IsBackground = true,
                Name = "nyxwm-conpty-reader"
            };
            _readerThread.Start();
        }

        private void ReadLoop(FileStream stream)
        {
            byte[] buffer = new byte[ReadBufferSize];

            try
            {
                while (true)
                {
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                        break;

                    EmitOutput(buffer.AsSpan(0, read));
                }
            }
            catch (Exception) when (Volatile.Read(ref _stopping) != 0)
            {
                // Expected during Dispose: the pseudoconsole closed the pipe.
            }
            catch (ObjectDisposedException)
            {
                // Same teardown race, reported as a disposed stream.
            }
            catch (Exception error)
            {
                RaiseFaulted(error);
            }
            finally
            {
                FlushDecoder();
                RaiseExited();
            }
        }

        private void EmitOutput(ReadOnlySpan<byte> data)
        {
            int characterCount = _decoder.GetCharCount(data, flush: false);
            if (characterCount > _decodeBuffer.Length)
                _decodeBuffer = new char[characterCount];

            int written = _decoder.GetChars(data, _decodeBuffer, flush: false);
            string text = written == 0 ? string.Empty : new string(_decodeBuffer, 0, written);
            byte[] copy = data.ToArray();

            EventHandler<TerminalOutputEventArgs>? handler = OutputReceived;
            if (handler is null)
                return;

            try
            {
                handler(this, new TerminalOutputEventArgs(copy, text));
            }
            catch (Exception error)
            {
                RaiseFaulted(error);
            }
        }

        private void FlushDecoder()
        {
            try
            {
                _decoder.GetChars(ReadOnlySpan<byte>.Empty, _decodeBuffer, flush: true);
            }
            catch (ArgumentException)
            {
                // Nothing buffered; nothing to flush.
            }
        }

        private void RaiseExited()
        {
            if (Interlocked.Exchange(ref _exitRaised, 1) != 0)
                return;

            try
            {
                Exited?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception error)
            {
                RaiseFaulted(error);
            }
        }

        private void RaiseFaulted(Exception error)
        {
            try
            {
                Faulted?.Invoke(this, new TerminalErrorEventArgs(error));
            }
            catch (Exception)
            {
                // Fault handlers must never take down the reader thread.
            }
        }

        private string BuildEnvironmentBlock()
        {
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key && entry.Value is string value)
                    variables[key] = value;
            }

            string root = _options.Msys2Root;
            string msystem = string.IsNullOrWhiteSpace(_options.Msystem) ? "UCRT64" : _options.Msystem;
            string subsystemBin = Path.Combine(root, msystem.ToLowerInvariant(), "bin");
            string usrBin = Path.Combine(root, "usr", "bin");
            string inheritedPath = variables.TryGetValue("PATH", out string? path) ? path : string.Empty;

            if (_options.UseMsysEnvironment)
            {
                variables["PATH"] = JoinPath(subsystemBin, usrBin, inheritedPath);
                variables["TERM"] = "xterm-256color";
                variables["COLORTERM"] = "truecolor";
                variables["LANG"] = "en_US.UTF-8";
                variables["MSYSTEM"] = msystem;
                variables["CHERE_INVOKING"] = "1";
                variables["MSYS2_PATH_TYPE"] = "inherit";
            }
            else
            {
                variables.Remove("MSYSTEM");
                variables.Remove("MSYS2_PATH_TYPE");
                variables.Remove("CHERE_INVOKING");
            }

            foreach (KeyValuePair<string, string> pair in _options.EnvironmentOverrides)
                variables[pair.Key] = pair.Value;

            var builder = new StringBuilder();
            foreach (KeyValuePair<string, string> pair in variables)
            {
                builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            }

            builder.Append('\0');
            return builder.ToString();
        }

        private static string JoinPath(params string[] parts)
        {
            var builder = new StringBuilder();

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                if (builder.Length > 0)
                    builder.Append(';');

                builder.Append(part);
            }

            return builder.ToString();
        }

        private void CloseHandle(ref IntPtr handle)
        {
            IntPtr value = handle;
            handle = IntPtr.Zero;

            if (value != IntPtr.Zero)
                ConPtyNative.CloseHandle(value);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(TerminalSession));
        }

        private static int ClampDimension(int value, int min, int max)
        {
            return Math.Clamp(value, min, max);
        }
    }
}
