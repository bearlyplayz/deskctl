using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using WinDeskCtl.Core.Uia;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Uia;

/// <summary>
/// UIA pattern calls. These take no coordinates, so they cannot miss, cannot race a moving
/// window, need no focus, and work while the window is occluded. This is why the
/// control model is semantic-first and SendInput is the fallback rather than the default.
/// </summary>
public static class SemanticActions
{
    public static async Task<Resolution> InvokeAsync(string handle, CancellationToken ct)
    {
        (nint abi, Resolution how) = HandleRegistry.Resolve(handle);

        try
        {
            await UiaSession.CallAsync<object?>(() =>
            {
                using PatternRef pattern = GetPattern(abi, UiaIds.UIA_InvokePatternId, handle, "invoke");
                InvokeUnsafe(pattern.Abi);
                return null;
            }, ct);

            return how;
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    public static async Task<Resolution> FillAsync(string handle, string value, CancellationToken ct)
    {
        (nint abi, Resolution how) = HandleRegistry.Resolve(handle);

        try
        {
            await UiaSession.CallAsync<object?>(() =>
            {
                using PatternRef pattern = GetPattern(abi, UiaIds.UIA_ValuePatternId, handle, "fill");
                SetValueUnsafe(pattern.Abi, value, handle);
                return null;
            }, ct);

            return how;
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    /// <summary>
    /// Polls until the element exists and is on screen. Replaces sleeping a guessed interval:
    /// a guess is either too short and flaky or too long and slow, and it is always both on a
    /// different machine.
    /// </summary>
    public static async Task<Resolution> WaitForAsync(string handle, TimeSpan timeout, CancellationToken ct)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        Exception? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                (nint abi, Resolution resolution) = HandleRegistry.Resolve(handle);
                try
                {
                    bool ready = await UiaSession.CallAsync(() => IsOnScreen(abi), ct);
                    if (ready) return resolution;
                }
                finally
                {
                    Marshal.Release(abi);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // Not there yet, or ambiguous right now. Both are normal mid-transition and
                // become permanent only if the deadline passes.
                last = ex;
            }

            // 100ms: fast enough that a human does not notice, slow enough that polling a
            // cross-process COM call does not itself load the target app.
            await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
        }

        throw new TimeoutException(
            $"Element '{handle}' did not appear within {timeout.TotalSeconds:0.#}s." +
            (last is not null ? $" Last attempt: {last.Message}" : ""));
    }

    private static bool IsOnScreen(nint abi)
    {
        try
        {
            Wrap(abi).get_CurrentIsOffscreen(out int offscreen);
            return offscreen == 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    /// <summary>Owns a pattern reference so every path releases it.</summary>
    private readonly struct PatternRef(nint abi) : IDisposable
    {
        internal nint Abi { get; } = abi;

        public void Dispose()
        {
            if (Abi != 0) Marshal.Release(Abi);
        }
    }

    private static PatternRef GetPattern(nint abi, int patternId, string handle, string verb)
    {
        IUIAutomationElement element = Wrap(abi);

        element.GetCurrentPattern(patternId, out nint pattern);

        if (pattern == 0)
        {
            throw new InvalidOperationException(
                $"Element '{handle}'{SafeName(element)} does not support '{verb}'. Check the element's " +
                "patterns in the snapshot, or fall back to a press with a coordinate.");
        }

        return new PatternRef(pattern);
    }

    private static unsafe void InvokeUnsafe(nint patternAbi) =>
        ComInterfaceMarshaller<IUIAutomationInvokePattern>.ConvertToManaged((void*)patternAbi)!.Invoke();

    private static unsafe void SetValueUnsafe(nint patternAbi, string value, string handle)
    {
        IUIAutomationValuePattern pattern = ComInterfaceMarshaller<IUIAutomationValuePattern>
            .ConvertToManaged((void*)patternAbi)!;

        pattern.get_CurrentIsReadOnly(out int readOnly);
        if (readOnly != 0)
        {
            // SetValue on a read-only element silently does nothing, which would have the batch
            // report success on a field that never changed.
            throw new InvalidOperationException($"Element '{handle}' is read-only.");
        }

        pattern.SetValue(value);
    }

    private static string SafeName(IUIAutomationElement element)
    {
        try
        {
            string name = element.get_CurrentName();
            return name.Length > 0 ? $" ('{name}')" : "";
        }
        catch (COMException)
        {
            return "";
        }
    }

    private static unsafe IUIAutomationElement Wrap(nint abi) =>
        ComInterfaceMarshaller<IUIAutomationElement>.ConvertToManaged((void*)abi)!;
}
