using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EDBrowser.Services;

public sealed record MemorySnapshot(
    long HostWorkingSetBytes,
    long HostPrivateBytes,
    long TotalWorkingSetBytes,
    long TotalPrivateBytes,
    int ProcessCount);

public sealed record ProcessMemoryInfo(
    int Pid,
    string Name,
    long WorkingSetBytes,
    long PrivateBytes,
    double CpuSeconds);

/// <summary>
/// Fast, zero-WMI process-tree memory measurement and working-set trimming using Toolhelp32 + Psapi.
/// </summary>
public static class MemoryManagerService
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public static MemorySnapshot CaptureProcessTreeSnapshot()
    {
        int rootPid = Environment.ProcessId;
        var treePids = CollectDescendantPids(rootPid);
        treePids.Add(rootPid);

        long hostWs = 0, hostPriv = 0, totalWs = 0, totalPriv = 0;
        int aliveCount = 0;

        foreach (int pid in treePids)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Refresh();
                long ws = proc.WorkingSet64;
                long priv = proc.PrivateMemorySize64;
                totalWs += ws;
                totalPriv += priv;
                aliveCount++;
                if (pid == rootPid)
                {
                    hostWs = ws;
                    hostPriv = priv;
                }
            }
            catch
            {
                // Process may have exited between snapshot and query
            }
        }

        return new MemorySnapshot(hostWs, hostPriv, totalWs, totalPriv, aliveCount);
    }

    public static List<ProcessMemoryInfo> CaptureDetailedProcessList()
    {
        int rootPid = Environment.ProcessId;
        var treePids = CollectDescendantPids(rootPid);
        treePids.Add(rootPid);

        var result = new List<ProcessMemoryInfo>();
        foreach (int pid in treePids)
        {
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.Refresh();
                double cpuSec = 0;
                try { cpuSec = proc.TotalProcessorTime.TotalSeconds; } catch { }
                result.Add(new ProcessMemoryInfo(
                    pid,
                    pid == rootPid ? $"{proc.ProcessName} (Ana Tarayıcı)" : proc.ProcessName,
                    proc.WorkingSet64,
                    proc.PrivateMemorySize64,
                    cpuSec));
            }
            catch { }
        }
        return result.OrderByDescending(x => x.WorkingSetBytes).ToList();
    }

    public static void TrimHostWorkingSet()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            EmptyWorkingSet(Process.GetCurrentProcess().Handle);
        }
        catch
        {
            // Ignore
        }
    }

    private static HashSet<int> CollectDescendantPids(int rootPid)
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1))
            return new HashSet<int>();

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
            if (Process32First(snap, ref entry))
            {
                do
                {
                    int pid = (int)entry.th32ProcessID;
                    int ppid = (int)entry.th32ParentProcessID;
                    if (!childrenByParent.TryGetValue(ppid, out var list))
                    {
                        list = new List<int>();
                        childrenByParent[ppid] = list;
                    }
                    list.Add(pid);
                } while (Process32Next(snap, ref entry));
            }
        }
        finally
        {
            CloseHandle(snap);
        }

        var result = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (!childrenByParent.TryGetValue(cur, out var kids))
                continue;
            foreach (var kid in kids)
            {
                if (result.Add(kid))
                    queue.Enqueue(kid);
            }
        }
        return result;
    }
}
