using System.Runtime.InteropServices;
using WinDeskCtl.Core.Launch;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Processes;

/// <summary>
/// A launched process, held open for as long as its window is being looked for.
/// </summary>
/// <remarks>
/// Keeping the handle rather than just the id is what makes the id trustworthy: Windows recycles
/// process ids freely, but not while a handle to the process is open. So for the life of this
/// object, the id cannot come to mean some unrelated process that started in the meantime.
/// </remarks>
internal sealed class LaunchedProcess(nint handle, int processId) : IDisposable
{
    private nint handle = handle;

    public int ProcessId { get; } = processId;

    /// <summary>The exit code, or null while the process is still running.</summary>
    public int? TryGetExitCode()
    {
        if (handle == 0) return null;
        if (!ProcessInterop.GetExitCodeProcess(handle, out uint code)) return null;

        // A process really can exit with 259, in which case this reads it as still running until
        // the wait times out. Distinguishing the two needs a wait on the handle; the cost of
        // getting it wrong is a launch that waits out its window timeout, so it is left alone.
        return code == ProcessInterop.STILL_ACTIVE ? null : unchecked((int)code);
    }

    public void Dispose()
    {
        if (handle == 0) return;
        ProcessInterop.CloseHandle(handle);
        handle = 0;
    }
}

/// <summary>
/// Starts a program with its stdout and stderr wired to a file.
/// </summary>
internal static class ProcessLauncher
{
    /// <summary>
    /// Resolves the log path, opens it, and starts the program. The log file exists by the time
    /// this returns even if the program writes nothing to it, so the path in the result always
    /// names something a caller can read.
    /// </summary>
    public static unsafe LaunchedProcess Start(LaunchInput input, string logPath)
    {
        IReadOnlyList<string> arguments = input.Arguments ?? [];
        IReadOnlyList<string> environment = input.Environment ?? [];

        string commandLine = CommandLine.Build(input.Path, arguments);
        string? workingDirectory = ResolveWorkingDirectory(input);
        string? environmentBlock = EnvironmentBlock.Build(CurrentEnvironment(), environment);

        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        nint log = OpenLog(logPath, input.AppendLog);
        nint nul = OpenNul();

        try
        {
            ProcessInterop.STARTUPINFOW startup = default;
            startup.cb = (uint)sizeof(ProcessInterop.STARTUPINFOW);
            startup.dwFlags = ProcessInterop.STARTF_USESTDHANDLES;

            // stdin is wired to NUL rather than left null: with STARTF_USESTDHANDLES set, a null
            // handle gives the child an invalid stdin, and a program that reads it gets an error
            // instead of the immediate end-of-file it expects from a non-interactive launch.
            startup.hStdInput = nul;
            startup.hStdOutput = log;
            startup.hStdError = log;

            uint flags = ProcessInterop.CREATE_NO_WINDOW;
            if (environmentBlock is not null) flags |= ProcessInterop.CREATE_UNICODE_ENVIRONMENT;

            ProcessInterop.PROCESS_INFORMATION info = default;

            // CreateProcessW writes into the command line buffer, so it gets a mutable copy
            // rather than the interned string literal a marshaller would hand it.
            char[] mutableCommandLine = [.. commandLine, '\0'];

            bool started;
            fixed (char* line = mutableCommandLine)
            fixed (char* directory = workingDirectory)
            fixed (char* block = environmentBlock)
            {
                // Handle inheritance has to be on for the std handles to reach the child, and it
                // is all-or-nothing: every inheritable handle in this process goes with it.
                // ponytail: an explicit PROC_THREAD_ATTRIBUTE_HANDLE_LIST would narrow it to the
                // three below, at the cost of the extended-startupinfo dance. Worth doing if
                // windeskctl ever opens inheritable handles of its own.
                started = ProcessInterop.CreateProcess(
                    applicationName: null,
                    commandLine: line,
                    processAttributes: 0,
                    threadAttributes: 0,
                    inheritHandles: true,
                    creationFlags: flags,
                    environment: block,
                    currentDirectory: directory,
                    startupInfo: &startup,
                    processInformation: &info);
            }

            if (!started) throw StartFailed(input.Path, Marshal.GetLastPInvokeError());

            // The thread handle is of no use here and leaks the primary thread if kept.
            ProcessInterop.CloseHandle(info.hThread);

            return new LaunchedProcess(info.hProcess, (int)info.dwProcessId);
        }
        finally
        {
            // These are this process's copies. The child holds its own, so closing them here does
            // not disturb its redirection — and not closing them would pin the log file open for
            // the rest of this process's life.
            ProcessInterop.CloseHandle(log);
            ProcessInterop.CloseHandle(nul);
        }
    }

    /// <summary>
    /// Where a launch's output goes when the caller did not say. Named for the program and the
    /// moment rather than the process id, because the file has to be open before there is a
    /// process to name it after.
    /// </summary>
    public static string DefaultLogPath(string program)
    {
        string name = Path.GetFileNameWithoutExtension(program);
        if (name.Length == 0) name = "launch";

        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        return Path.Join(Path.GetTempPath(), "windeskctl", $"{name}-{stamp}.log");
    }

    private static nint OpenLog(string path, bool append)
    {
        ProcessInterop.SECURITY_ATTRIBUTES security = default;
        security.nLength = (uint)Marshal.SizeOf<ProcessInterop.SECURITY_ATTRIBUTES>();
        security.bInheritHandle = 1;

        // Append mode is FILE_APPEND_DATA rather than a seek to the end: every write then lands
        // at the end of the file as one operation, which is what keeps a program that writes to
        // stdout and stderr from overwriting its own output.
        uint access = append
            ? ProcessInterop.FILE_APPEND_DATA | ProcessInterop.SYNCHRONIZE
            : ProcessInterop.GENERIC_WRITE | ProcessInterop.SYNCHRONIZE;
        uint disposition = append ? ProcessInterop.OPEN_ALWAYS : ProcessInterop.CREATE_ALWAYS;

        nint handle;
        unsafe
        {
            // Shared for reading and writing so the log can be tailed while the program runs.
            handle = ProcessInterop.CreateFile(
                path,
                access,
                ProcessInterop.FILE_SHARE_READ | ProcessInterop.FILE_SHARE_WRITE | ProcessInterop.FILE_SHARE_DELETE,
                &security,
                disposition,
                ProcessInterop.FILE_ATTRIBUTE_NORMAL,
                0);
        }

        if (handle == ProcessInterop.InvalidHandleValue)
        {
            throw new IOException(
                $"Cannot open log file '{path}' (error {Marshal.GetLastPInvokeError()}).");
        }

        return handle;
    }

    private static nint OpenNul()
    {
        ProcessInterop.SECURITY_ATTRIBUTES security = default;
        security.nLength = (uint)Marshal.SizeOf<ProcessInterop.SECURITY_ATTRIBUTES>();
        security.bInheritHandle = 1;

        nint handle;
        unsafe
        {
            handle = ProcessInterop.CreateFile(
                "NUL",
                ProcessInterop.GENERIC_READ,
                ProcessInterop.FILE_SHARE_READ | ProcessInterop.FILE_SHARE_WRITE,
                &security,
                ProcessInterop.OPEN_EXISTING,
                ProcessInterop.FILE_ATTRIBUTE_NORMAL,
                0);
        }

        if (handle == ProcessInterop.InvalidHandleValue)
        {
            throw new IOException($"Cannot open NUL (error {Marshal.GetLastPInvokeError()}).");
        }

        return handle;
    }

    /// <summary>
    /// The executable's own directory, when there is one to know. A program launched over MCP
    /// would otherwise inherit whatever directory the server was started in, which is arbitrary
    /// and breaks anything that reads a file relative to itself.
    /// </summary>
    private static string? ResolveWorkingDirectory(LaunchInput input)
    {
        if (input.WorkingDirectory is { Length: > 0 } explicitly)
        {
            if (!Directory.Exists(explicitly))
            {
                throw new ArgumentException(
                    $"Working directory '{explicitly}' does not exist.", nameof(input));
            }
            return explicitly;
        }

        // A bare program name is resolved against PATH by CreateProcess, so its directory is not
        // knowable here. Inheriting this process's directory is the only option left.
        return Path.IsPathRooted(input.Path) ? Path.GetDirectoryName(Path.GetFullPath(input.Path)) : null;
    }

    private static IEnumerable<KeyValuePair<string, string>> CurrentEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                yield return new KeyValuePair<string, string>(name, value);
            }
        }
    }

    private static Exception StartFailed(string program, int error) => error switch
    {
        ProcessInterop.ERROR_FILE_NOT_FOUND or ProcessInterop.ERROR_PATH_NOT_FOUND =>
            new ArgumentException(
                $"Cannot find '{program}'. Give a full path, or a bare name that is on PATH.", nameof(program)),

        ProcessInterop.ERROR_ELEVATION_REQUIRED =>
            new NotSupportedException(
                $"'{program}' requires elevation. windeskctl cannot launch it, and could not " +
                "automate its windows afterwards either — UIPI blocks input to a higher " +
                "integrity level than its own."),

        _ => new InvalidOperationException($"Cannot start '{program}' (error {error})."),
    };
}
