using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Uia;

/// <summary>
/// The process-wide IUIAutomation instance, plus the timeout wrapper every UIA call goes through.
/// </summary>
/// <remarks>
/// One instance for the process: creating it is expensive, and attaching UIA to a Chromium window
/// makes that app enable its accessibility tree — a cost paid once rather than per call.
///
/// Handles are valid for the process lifetime. Under stdio the process IS the client session,
/// which is the natural scope.
/// </remarks>
public sealed class UiaSession
{
    private static readonly Lazy<UiaSession> Instance =
        new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    public static UiaSession Current => Instance.Value;

    /// <summary>
    /// UIA offers no per-call timeout, and a call into an unresponsive app blocks forever
    ///. Five seconds is well past any healthy call — a scoped FindAll on a Chromium
    /// window measures in tens of milliseconds.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    internal IUIAutomation Automation { get; }

    /// <summary>
    /// The control view: the elements a user can interact with, minus the raw view's layout
    /// scaffolding. This is the view a snapshot walks.
    /// </summary>
    /// <remarks>
    /// Fetched once. A condition is an immutable value object, and re-fetching it per element
    /// would add a cross-process call for every node of every walk to obtain the same object.
    /// Never released: it lives exactly as long as the session, which ends at process exit.
    /// </remarks>
    internal nint ControlViewCondition { get; }

    private UiaSession(IUIAutomation automation, nint controlViewCondition)
    {
        Automation = automation;
        ControlViewCondition = controlViewCondition;
    }

    private static unsafe UiaSession Create()
    {
        Marshal.ThrowExceptionForHR(UiaFactory.CoCreateInstance(
            in UiaFactory.CLSID_CUIAutomation, 0, UiaFactory.CLSCTX_INPROC_SERVER,
            in UiaFactory.IID_IUIAutomation, out nint abi));

        try
        {
            // The RCW takes its own reference, so the raw one is dropped here rather than held
            // for a process lifetime that ends only at exit anyway.
            IUIAutomation automation = ComInterfaceMarshaller<IUIAutomation>.ConvertToManaged((void*)abi)!;
            automation.get_ControlViewCondition(out nint condition);

            if (condition == 0)
            {
                throw new InvalidOperationException(
                    "UI Automation returned no control-view condition; the tree cannot be walked.");
            }

            return new UiaSession(automation, condition);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    /// <summary>
    /// Runs a UIA call with a timeout.
    /// </summary>
    /// <remarks>
    /// The work runs on a thread-pool thread — MTA by default, which is what UIA's free-threaded
    /// marshalling wants — and the timeout is enforced by abandoning the wait, not the call.
    /// A hung call therefore leaks its thread until the target app recovers. That is the
    /// accepted cost: the alternative is blocking the whole server on one wedged app, and COM
    /// gives no way to cancel a synchronous cross-process call.
    /// </remarks>
    public static async Task<T> CallAsync<T>(Func<T> work, CancellationToken ct, TimeSpan? timeout = null)
    {
        Task<T> task = Task.Run(work, CancellationToken.None);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            return await task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"A UI Automation call did not return within {(timeout ?? DefaultTimeout).TotalSeconds:0.#}s. " +
                "The target application is most likely unresponsive.");
        }
    }
}
