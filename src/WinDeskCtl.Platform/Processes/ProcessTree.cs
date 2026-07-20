using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Processes;

/// <summary>
/// Which processes descend from a launched one.
/// </summary>
/// <remarks>
/// This is what ties a window back to a launch. Matching on executable name instead would be
/// wrong in both directions: a program that spawns a differently-named worker to own the real
/// window would be missed, and a second copy of the same program started by someone else would
/// be claimed. Lineage is exact for both.
/// </remarks>
internal static class ProcessTree
{
    /// <summary>
    /// The given process and everything descended from it, at this moment. Re-read rather than
    /// cached, because the children that matter are usually spawned after the launch returns.
    /// </summary>
    public static HashSet<int> DescendantsOf(int rootProcessId)
    {
        HashSet<int> tree = [rootProcessId];

        nint snapshot = ProcessInterop.CreateToolhelp32Snapshot(ProcessInterop.TH32CS_SNAPPROCESS, 0);
        if (snapshot == ProcessInterop.InvalidHandleValue) return tree;

        try
        {
            // ponytail: parent ids are unvalidated, so a recycled id can graft an unrelated
            // process onto the tree. Comparing process creation times would close it; the window
            // is one launch's wait, and the cost is a spurious extra candidate the caller can
            // see and reject.
            Dictionary<int, List<int>> childrenByParent = [];

            ProcessInterop.PROCESSENTRY32W entry = default;
            unsafe { entry.dwSize = (uint)sizeof(ProcessInterop.PROCESSENTRY32W); }

            if (!ProcessInterop.Process32First(snapshot, ref entry)) return tree;

            do
            {
                int pid = (int)entry.th32ProcessID;
                int parent = (int)entry.th32ParentProcessID;

                // The idle process parents itself, which would loop the walk below forever.
                if (pid == parent) continue;

                if (!childrenByParent.TryGetValue(parent, out List<int>? children))
                {
                    childrenByParent[parent] = children = [];
                }
                children.Add(pid);
            }
            while (ProcessInterop.Process32Next(snapshot, ref entry));

            Queue<int> pending = new([rootProcessId]);
            while (pending.Count > 0)
            {
                if (!childrenByParent.TryGetValue(pending.Dequeue(), out List<int>? children)) continue;

                foreach (int child in children)
                {
                    // The visited set doubles as the cycle guard: a recycled id can make the
                    // parent graph cyclic, and an unguarded walk would not terminate.
                    if (tree.Add(child)) pending.Enqueue(child);
                }
            }

            return tree;
        }
        finally
        {
            ProcessInterop.CloseHandle(snapshot);
        }
    }
}
