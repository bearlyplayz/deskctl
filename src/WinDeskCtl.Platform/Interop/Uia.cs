using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace WinDeskCtl.Platform.Interop;

/// <summary>
/// IUIAutomation, the root object. Instantiated from CLSID_CUIAutomation.
/// </summary>
/// <remarks>
/// Declared by hand with [GeneratedComInterface] rather than taken from a TLB or from
/// System.Windows.Automation: source-generated ComWrappers is the only NativeAOT-clean COM path
///.
///
/// Method order IS the vtable and matches UIAutomationClient.h exactly. Unused methods are
/// declared as placeholders because a slot cannot be skipped — omitting one shifts every method
/// after it, which does not fail the build and instead calls the wrong function at runtime.
/// Placeholders keep ABI-correct blittable signatures so that a future caller inherits a
/// working declaration rather than a trap.
/// </remarks>
[GeneratedComInterface]
[Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee")]
internal partial interface IUIAutomation
{
    // Slots 3-4
    void CompareElements(nint a, nint b, out int same);
    void CompareRuntimeIds(nint a, nint b, out int same);

    // Slot 5
    void GetRootElement(out nint root);

    // Slot 6
    void ElementFromHandle(nint hwnd, out nint element);

    // Slots 7-8. POINT is two LONGs; on x64 an 8-byte struct passes exactly as an integer would.
    void ElementFromPoint(long point, out nint element);
    void GetFocusedElement(out nint element);

    // Slots 9-12 — build-cache variants, unused
    void GetRootElementBuildCache(nint cacheRequest, out nint root);
    void ElementFromHandleBuildCache(nint hwnd, nint cacheRequest, out nint element);
    void ElementFromPointBuildCache(long point, nint cacheRequest, out nint element);
    void GetFocusedElementBuildCache(nint cacheRequest, out nint element);

    // Slots 13-16 — tree walkers, unused
    void CreateTreeWalker(nint condition, out nint walker);
    void get_ControlViewWalker(out nint walker);
    void get_ContentViewWalker(out nint walker);
    void get_RawViewWalker(out nint walker);

    // Slot 17
    void get_RawViewCondition(out nint condition);

    /// <summary>The control view: the elements a user can interact with, minus the raw view's
    /// layout scaffolding. This is the view a snapshot walks.</summary>
    void get_ControlViewCondition(out nint condition);

    // Slot 19
    void get_ContentViewCondition(out nint condition);

    // Slots 20-22
    void CreateCacheRequest(out nint request);
    void CreateTrueCondition(out nint condition);
    void CreateFalseCondition(out nint condition);
}

/// <summary>
/// IUIAutomationElement. Only the methods windeskctl calls are given real signatures; the rest are
/// placeholders holding their vtable slots.
/// </summary>
/// <remarks>
/// StringMarshalling is BSTR-custom for the whole interface: every string this interface returns
/// is a BSTR, and BSTR is not a plain wide pointer — it is length-prefixed and allocated by the
/// COM allocator, so the caller must free it. The marshaller does both.
/// </remarks>
[GeneratedComInterface(
    StringMarshalling = StringMarshalling.Custom,
    StringMarshallingCustomType = typeof(BStrStringMarshaller))]
[Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e")]
internal partial interface IUIAutomationElement
{
    // Slot 3 — genuinely first, before GetRuntimeId
    void SetFocus();

    // Slot 4 — SAFEARRAY(int)*, opaque here
    void GetRuntimeId(out nint runtimeIds);

    // Slots 5-6
    void FindFirst(int scope, nint condition, out nint found);
    void FindAll(int scope, nint condition, out nint found);

    // Slots 7-9
    void FindFirstBuildCache(int scope, nint condition, nint cacheRequest, out nint found);
    void FindAllBuildCache(int scope, nint condition, nint cacheRequest, out nint found);
    void BuildUpdatedCache(nint cacheRequest, out nint updated);

    // Slots 10-13 — VARIANT* out params. Kept as raw pointers: VARIANT marshalling is not
    // expressible in source-generated COM, and no caller needs these.
    void GetCurrentPropertyValue(int propertyId, nint value);
    void GetCurrentPropertyValueEx(int propertyId, int ignoreDefault, nint value);
    void GetCachedPropertyValue(int propertyId, nint value);
    void GetCachedPropertyValueEx(int propertyId, int ignoreDefault, nint value);

    // Slots 14-15
    void GetCurrentPatternAs(int patternId, in Guid iid, out nint pattern);
    void GetCachedPatternAs(int patternId, in Guid iid, out nint pattern);

    // Slot 16
    void GetCurrentPattern(int patternId, out nint pattern);

    // Slots 17-19
    void GetCachedPattern(int patternId, out nint pattern);
    void GetCachedParent(out nint parent);
    void GetCachedChildren(out nint children);

    // Slots 20-23
    void get_CurrentProcessId(out int pid);
    void get_CurrentControlType(out int controlType);
    string get_CurrentLocalizedControlType();
    string get_CurrentName();

    // Slots 24-28
    string get_CurrentAcceleratorKey();
    string get_CurrentAccessKey();
    void get_CurrentHasKeyboardFocus(out int value);
    void get_CurrentIsKeyboardFocusable(out int value);
    void get_CurrentIsEnabled(out int value);

    // Slot 29
    string get_CurrentAutomationId();

    // Slots 30-42
    string get_CurrentClassName();
    string get_CurrentHelpText();
    void get_CurrentCulture(out int culture);
    void get_CurrentIsControlElement(out int value);
    void get_CurrentIsContentElement(out int value);
    void get_CurrentIsPassword(out int value);
    void get_CurrentNativeWindowHandle(out nint hwnd);
    string get_CurrentItemType();
    void get_CurrentIsOffscreen(out int value);
    void get_CurrentOrientation(out int value);
    string get_CurrentFrameworkId();
    void get_CurrentIsRequiredForForm(out int value);
    string get_CurrentItemStatus();

    /// <summary>
    /// Slot 43. A Win32 RECT of four LONGs, in absolute screen pixels, whose far edges are
    /// absolute coordinates rather than a width and height.
    /// </summary>
    /// <remarks>
    /// Not the double-based UiaRect: that type belongs to the raw provider API and does not
    /// appear in UIAutomationClient.h at all. The distinction is invisible to the compiler and
    /// surfaces only as a garbage rect — the four LONGs get reinterpreted as denormal doubles.
    /// </remarks>
    void get_CurrentBoundingRectangle(out User32.RECT rect);
}

/// <summary>
/// IUIAutomationElementArray — a returned collection. Not a .NET collection: it is a live COM
/// object and each GetElement is a cross-process call.
/// </summary>
[GeneratedComInterface]
[Guid("14314595-b4bc-4055-95f2-58f2e42c9855")]
internal partial interface IUIAutomationElementArray
{
    void get_Length(out int length);
    void GetElement(int index, out nint element);
}

[GeneratedComInterface]
[Guid("fb377fbe-8ea6-46d5-9c73-6499642d3059")]
internal partial interface IUIAutomationInvokePattern
{
    /// <summary>Takes no coordinates: it cannot miss, cannot race a moving window, needs no
    /// focus, and works while occluded.</summary>
    void Invoke();
}

/// <remarks>SetValue takes a BSTR, not a wide pointer — the header is explicit, and passing a
/// plain LPWSTR would have UIA read a length prefix out of unrelated memory.</remarks>
[GeneratedComInterface(
    StringMarshalling = StringMarshalling.Custom,
    StringMarshallingCustomType = typeof(BStrStringMarshaller))]
[Guid("a94cd8b1-0844-4cd6-9d2d-640537ab39e9")]
internal partial interface IUIAutomationValuePattern
{
    void SetValue(string value);

    string get_CurrentValue();

    void get_CurrentIsReadOnly(out int readOnly);
}

/// <summary>
/// UIA ids, transcribed from UIAutomationClient.h.
/// </summary>
internal static class UiaIds
{
    internal const int TreeScope_Children = 2;
    internal const int TreeScope_Descendants = 4;

    internal const int UIA_InvokePatternId = 10000;
    internal const int UIA_ValuePatternId = 10002;
    internal const int UIA_TogglePatternId = 10015;
    internal const int UIA_ExpandCollapsePatternId = 10005;
    internal const int UIA_SelectionItemPatternId = 10010;

    private static readonly Dictionary<int, string> ControlTypes = new()
    {
        [50000] = "button",
        [50001] = "calendar",
        [50002] = "checkbox",
        [50003] = "combobox",
        [50004] = "edit",
        [50005] = "hyperlink",
        [50006] = "image",
        [50007] = "listitem",
        [50008] = "list",
        [50009] = "menu",
        [50010] = "menubar",
        [50011] = "menuitem",
        [50012] = "progressbar",
        [50013] = "radiobutton",
        [50014] = "scrollbar",
        [50015] = "slider",
        [50016] = "spinner",
        [50017] = "statusbar",
        [50018] = "tab",
        [50019] = "tabitem",
        [50020] = "text",
        [50021] = "toolbar",
        [50022] = "tooltip",
        [50023] = "tree",
        [50024] = "treeitem",
        [50025] = "custom",
        [50026] = "group",
        [50027] = "thumb",
        [50028] = "datagrid",
        [50029] = "dataitem",
        [50030] = "document",
        [50031] = "splitbutton",
        [50032] = "window",
        [50033] = "pane",
        [50034] = "header",
        [50035] = "headeritem",
        [50036] = "table",
        [50037] = "titlebar",
        [50038] = "separator",
        [50039] = "semanticzoom",
        [50040] = "appbar",
    };

    internal static string ControlTypeName(int id) =>
        ControlTypes.TryGetValue(id, out string? n) ? n : $"unknown-{id}";
}

internal static partial class UiaFactory
{
    internal static readonly Guid CLSID_CUIAutomation = new("ff48dba4-60ef-4201-aa87-54103eef594e");
    internal static readonly Guid IID_IUIAutomation = new("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee");

    internal const int CLSCTX_INPROC_SERVER = 1;

    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(
        in Guid clsid, nint outer, int context, in Guid iid, out nint instance);
}
