using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mortz.E2E.Tests.Harness;

/// <summary>
/// A Windows job object that kills every child when the handle closes, which
/// includes the testhost being hard-killed before any pipe can signal EOF.
/// Non-Windows gets a no-op object; there the stdin lifeline is the only layer
/// (owed work).
/// </summary>
public sealed class ProcessReaper : IDisposable
{
    private const int JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private readonly nint _job;
    private bool _disposed;

    private ProcessReaper(nint job) => _job = job;

    public static ProcessReaper Create()
    {
        if (!OperatingSystem.IsWindows())
            return new ProcessReaper(0);

        nint job = CreateJobObject(0, null);
        if (job == 0)
            throw new E2EProcessException("Could not create the E2E job object.");

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = default;
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);
            if (!SetInformationJobObject(job, JOB_OBJECT_EXTENDED_LIMIT_INFORMATION, buffer, (uint)size))
            {
                CloseHandle(job);
                throw new E2EProcessException("Could not arm the E2E job object.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return new ProcessReaper(job);
    }

    public void Adopt(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (_job == 0)
            return;
        // A process that already exited cannot be assigned, and does not need to be.
        if (process.HasExited)
            return;
        AssignProcessToJobObject(_job, process.Handle);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_job != 0)
            CloseHandle(_job);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        nint job, int infoClass, nint info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);
}
