using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Numerics;
using Microsoft.Win32.SafeHandles;

namespace CMCLauncher;

public sealed partial class WindowsLauncherPlatform
{
    const string MemoryOptimizationWorkerSwitch = "--optimize-system-memory-worker";
    const int WorkerResultBase = 0x100;
    const int WorkerResultMask = 0xFF;
    const int UacCancelledError = 1223;

    public override bool SupportsMemoryOptimization => true;

    const int SystemMemoryListInformation = 80;
    const int SystemCombinePhysicalMemoryInformation = 130;
    const int SystemRegistryReconciliationInformation = 155;

    [Flags]
    enum MemoryOperation
    {
        None = 0,
        SystemWorkingSets = 1 << 0,
        SessionWorkingSets = 1 << 1,
        FileCache = 1 << 2,
        ModifiedPages = 1 << 3,
        StandbyPages = 1 << 4,
        LowPriorityStandbyPages = 1 << 5,
        RegistryReconciliation = 1 << 6,
        CombinePhysicalPages = 1 << 7,
    }

    [DllImport("kernel32.dll")]
    static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("kernel32.dll")]
    static extern bool K32EmptyWorkingSet(SafeProcessHandle process);

    enum MemoryListCommand
    {
        EmptyWorkingSets = 2,
        FlushModifiedPages = 3,
        PurgeStandbyPages = 4,
        PurgeLowPriorityStandbyPages = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryCombineInformation
    {
        public IntPtr Handle;
        public UIntPtr PagesCombined;
        public uint Flags;
    }

    [DllImport("ntdll.dll")]
    static extern int RtlAdjustPrivilege(
        uint privilege,
        [MarshalAs(UnmanagedType.U1)] bool enable,
        [MarshalAs(UnmanagedType.U1)] bool currentThread,
        [MarshalAs(UnmanagedType.U1)] out bool previouslyEnabled
    );

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    static extern int SetMemoryListInformation(int informationClass, ref MemoryListCommand command, int size);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    static extern int SetCombineInformation(int informationClass, ref MemoryCombineInformation data, int size);

    [DllImport("ntdll.dll", EntryPoint = "NtSetSystemInformation")]
    static extern int SetSystemInformation(int informationClass, IntPtr data, int size);

    [DllImport("kernel32.dll")]
    static extern bool SetSystemFileCacheSize(UIntPtr minimum, UIntPtr maximum, uint flags);

    public static bool TryRunMemoryOptimizationWorker(string[] args, out int exitCode)
    {
        exitCode = 1;
        if (args.Length != 1 || args[0] != MemoryOptimizationWorkerSwitch)
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            // These privileges apply only to the short-lived elevated worker process.
            var completed = OptimizeSystemMemory();
            exitCode = WorkerResultBase | (int)completed;
        }

        return true;
    }

    static MemoryOperation OptimizeSystemMemory()
    {
        const uint increaseQuotaPrivilege = 5;
        const uint profileSingleProcessPrivilege = 13;
        var canProfile = RtlAdjustPrivilege(profileSingleProcessPrivilege, true, false, out _) == 0;
        var canAdjustCache = RtlAdjustPrivilege(increaseQuotaPrivilege, true, false, out _) == 0;
        var completed = MemoryOperation.None;

        void RunMemoryListCommand(MemoryListCommand command, MemoryOperation operation)
        {
            if (SetMemoryListInformation(SystemMemoryListInformation, ref command, sizeof(int)) == 0)
                completed |= operation;
        }

        if (canProfile)
            RunMemoryListCommand(MemoryListCommand.EmptyWorkingSets, MemoryOperation.SystemWorkingSets);

        // Some Windows builds reject the system-wide command. An independent pass also reaches
        // processes the system can leave resident after that command successfully returns.
        if (TrimInteractiveSessionWorkingSets())
            completed |= MemoryOperation.SessionWorkingSets;

        if (canAdjustCache && SetSystemFileCacheSize(nuint.MaxValue, nuint.MaxValue, 0))
            completed |= MemoryOperation.FileCache;

        if (canProfile)
        {
            RunMemoryListCommand(MemoryListCommand.FlushModifiedPages, MemoryOperation.ModifiedPages);
            RunMemoryListCommand(MemoryListCommand.PurgeStandbyPages, MemoryOperation.StandbyPages);
            RunMemoryListCommand(MemoryListCommand.PurgeLowPriorityStandbyPages, MemoryOperation.LowPriorityStandbyPages);

            if (SetSystemInformation(SystemRegistryReconciliationInformation, IntPtr.Zero, 0) == 0)
                completed |= MemoryOperation.RegistryReconciliation;

            var combine = new MemoryCombineInformation();
            if (SetCombineInformation(
                SystemCombinePhysicalMemoryInformation, ref combine, Marshal.SizeOf<MemoryCombineInformation>()
            ) == 0)
                completed |= MemoryOperation.CombinePhysicalPages;
        }

        return completed;
    }

    static bool TrimInteractiveSessionWorkingSets()
    {
        var workerId = (uint)Environment.ProcessId;
        if (!ProcessIdToSessionId(workerId, out var session) || session == 0)
            return false;

        const uint processSetQuota = 0x0100;
        const uint processQueryLimitedInformation = 0x1000;
        var processes = Process.GetProcesses();
        var trimmed = false;
        try
        {
            foreach (var process in processes)
            {
                var id = (uint)process.Id;
                if (id == workerId || !ProcessIdToSessionId(id, out var targetSession) ||
                    targetSession != session)
                    continue;
                using var handle = OpenProcess(processSetQuota | processQueryLimitedInformation, false, id);
                if (!handle.IsInvalid && K32EmptyWorkingSet(handle))
                    trimmed = true;
            }
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }

        return trimmed;
    }

    static readonly object memoryOptimizationGate = new();

    public override MemoryOptimizationResult? OptimizeMemoryBeforeLaunch(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Monitor.TryEnter(memoryOptimizationGate))
            return null;
        try
        {
            return OptimizeMemory(ct);
        }
        finally
        {
            Monitor.Exit(memoryOptimizationGate);
        }
    }

    public override MemoryOptimizationResult OptimizeMemory(CancellationToken ct)
    {
        lock (memoryOptimizationGate)
        {
            ct.ThrowIfCancellationRequested();
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot locate the launcher executable.");
            var launch = new ProcessStartInfo(executable)
            {
                Arguments = MemoryOptimizationWorkerSwitch,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Environment.SystemDirectory,
            };

            var before = PhysicalMemory()?.AvailableMb;
            try
            {
                using var worker = Process.Start(launch)
                    ?? throw new InvalidOperationException("Could not start the memory optimization worker.");
                // Keep the task running until all native operations have returned and the worker exits.
                worker.WaitForExit();
                ct.ThrowIfCancellationRequested();
                var completed = worker.ExitCode & WorkerResultMask;
                if ((worker.ExitCode & ~WorkerResultMask) != WorkerResultBase || completed == 0)
                    throw new InvalidOperationException("The elevated memory optimization did not complete any system operation.");
                return new(
                    BitOperations.PopCount((uint)completed), before, PhysicalMemory()?.AvailableMb,
                    ((MemoryOperation)completed).ToString()
                );
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == UacCancelledError)
            {
                throw new MemoryOptimizationDeclinedException();
            }
        }
    }
}
