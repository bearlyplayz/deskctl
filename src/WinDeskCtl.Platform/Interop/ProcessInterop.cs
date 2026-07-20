using System.Runtime.InteropServices;

namespace WinDeskCtl.Platform.Interop;

/// <summary>
/// Process creation with inherited standard handles.
/// </summary>
/// <remarks>
/// .NET's Process class can redirect a child's output to a pipe but not to a file, and a pipe is
/// the wrong shape here: windeskctl exits while the launched program keeps running, and a pipe
/// with no reader blocks the writer once its buffer fills — a few kilobytes of output in, the
/// program silently hangs. Handing the child a file handle instead makes the OS the writer's
/// counterparty, so nothing has to stay alive to drain it.
/// </remarks>
internal static partial class ProcessInterop
{
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x0000_0400;
    internal const uint CREATE_NO_WINDOW = 0x0800_0000;

    internal const uint STARTF_USESTDHANDLES = 0x0000_0100;

    internal const uint GENERIC_READ = 0x8000_0000;
    internal const uint GENERIC_WRITE = 0x4000_0000;
    internal const uint FILE_APPEND_DATA = 0x0000_0004;
    internal const uint SYNCHRONIZE = 0x0010_0000;

    internal const uint FILE_SHARE_READ = 0x0000_0001;
    internal const uint FILE_SHARE_WRITE = 0x0000_0002;
    internal const uint FILE_SHARE_DELETE = 0x0000_0004;

    internal const uint CREATE_ALWAYS = 2;
    internal const uint OPEN_ALWAYS = 4;
    internal const uint OPEN_EXISTING = 3;

    internal const uint FILE_ATTRIBUTE_NORMAL = 0x0000_0080;

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_PATH_NOT_FOUND = 3;
    internal const int ERROR_ELEVATION_REQUIRED = 740;

    /// <summary>GetExitCodeProcess reports this while the process is still running.</summary>
    internal const uint STILL_ACTIVE = 259;

    internal const uint TH32CS_SNAPPROCESS = 0x0000_0002;

    internal static readonly nint InvalidHandleValue = -1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SECURITY_ATTRIBUTES
    {
        internal uint nLength;
        internal nint lpSecurityDescriptor;

        /// <summary>Win32 BOOL: 1 makes the returned handle inheritable by a child process.</summary>
        internal int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct STARTUPINFOW
    {
        internal uint cb;
        internal nint lpReserved;
        internal nint lpDesktop;
        internal nint lpTitle;
        internal uint dwX;
        internal uint dwY;
        internal uint dwXSize;
        internal uint dwYSize;
        internal uint dwXCountChars;
        internal uint dwYCountChars;
        internal uint dwFillAttribute;
        internal uint dwFlags;
        internal ushort wShowWindow;
        internal ushort cbReserved2;
        internal nint lpReserved2;
        internal nint hStdInput;
        internal nint hStdOutput;
        internal nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        internal nint hProcess;
        internal nint hThread;
        internal uint dwProcessId;
        internal uint dwThreadId;
    }

    /// <summary>
    /// Blittable by construction — the executable name is an inline fixed buffer, since the
    /// source-generated <c>LibraryImport</c> path cannot emit a <c>ByValTStr</c> marshaller.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PROCESSENTRY32W
    {
        /// <summary>MAX_PATH. The Win32 struct layout depends on this exact length.</summary>
        internal const int MaxPath = 260;

        internal uint dwSize;
        internal uint cntUsage;
        internal uint th32ProcessID;
        internal nuint th32DefaultHeapID;
        internal uint th32ModuleID;
        internal uint cntThreads;
        internal uint th32ParentProcessID;
        internal int pcPriClassBase;
        internal uint dwFlags;
        internal fixed char szExeFile[MaxPath];
    }

    /// <summary>
    /// The command line is a <c>char*</c> rather than a string because CreateProcessW writes
    /// into the buffer it is given, so the caller must own writable, pinned memory.
    /// </summary>
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool CreateProcess(
        char* applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        void* environment,
        char* currentDirectory,
        STARTUPINFOW* startupInfo,
        PROCESS_INFORMATION* processInformation);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    internal static unsafe partial nint CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        SECURITY_ATTRIBUTES* securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(nint process, out uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32First(nint snapshot, ref PROCESSENTRY32W entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool Process32Next(nint snapshot, ref PROCESSENTRY32W entry);
}
