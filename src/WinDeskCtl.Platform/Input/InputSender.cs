using System.Runtime.InteropServices;
using WinDeskCtl.Core.Frames;
using WinDeskCtl.Core.Input;
using WinDeskCtl.Platform.Interop;

namespace WinDeskCtl.Platform.Input;

/// <summary>
/// Turns planned steps into SendInput arrays. The only Win32 in the input path — the grammar,
/// planning, interpolation, held-set, and normalization are all pure and tested without it.
/// </summary>
public static class InputSender
{
    /// <param name="resolvePoint">Resolves a frame-qualified string such as "win:100@400,200"
    /// into a virtual-desktop point.</param>
    /// <returns>The number of events the OS accepted.</returns>
    public static int Send(IReadOnlyList<Step> steps, FrameRect virtualBounds, Func<string, Point> resolvePoint)
    {
        List<SendInputInterop.INPUT> inputs = [];

        foreach (Step step in steps)
        {
            switch (step)
            {
                case Step.Move move:
                    inputs.Add(MouseMove(resolvePoint(move.To), virtualBounds));
                    break;

                case Step.Down { Target: KeyRef k }:
                    inputs.Add(Key(KeyMap.Resolve(k.Name), up: false));
                    break;

                case Step.Up { Target: KeyRef k }:
                    inputs.Add(Key(KeyMap.Resolve(k.Name), up: true));
                    break;

                case Step.Press { Target: KeyRef k }:
                    inputs.Add(Key(KeyMap.Resolve(k.Name), up: false));
                    inputs.Add(Key(KeyMap.Resolve(k.Name), up: true));
                    break;

                case Step.Down { Target: ButtonRef b }:
                    inputs.Add(Button(b.Button, up: false));
                    break;

                case Step.Up { Target: ButtonRef b }:
                    inputs.Add(Button(b.Button, up: true));
                    break;

                case Step.Press { Target: ButtonRef b } press:
                    if (press.To is not null)
                    {
                        inputs.Add(MouseMove(resolvePoint(press.To), virtualBounds));
                    }
                    inputs.Add(Button(b.Button, up: false));
                    inputs.Add(Button(b.Button, up: true));
                    break;

                case Step.Scroll scroll:
                    if (scroll.At is not null)
                    {
                        inputs.Add(MouseMove(resolvePoint(scroll.At), virtualBounds));
                    }
                    if (scroll.Dy != 0) inputs.Add(Wheel(scroll.Dy, horizontal: false));
                    if (scroll.Dx != 0) inputs.Add(Wheel(scroll.Dx, horizontal: true));
                    break;

                case Step.Text text:
                    inputs.AddRange(Unicode(text.Value));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"{step.GetType().Name} is not a synthetic step and should not have reached the sender.");
            }
        }

        if (inputs.Count == 0) return 0;

        SendInputInterop.INPUT[] array = [.. inputs];
        int size = Marshal.SizeOf<SendInputInterop.INPUT>();

        uint sent = SendInputInterop.SendInput((uint)array.Length, array, size);

        if (sent != array.Length)
        {
            // A partial send means UIPI blocked it — the foreground window is elevated and this
            // process is not. No library fixes this; it is a Windows security boundary.
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput accepted {sent} of {array.Length} events (error {err}). " +
                "The target window is most likely running elevated, which UIPI blocks windeskctl from reaching.");
        }

        return (int)sent;
    }

    private static SendInputInterop.INPUT MouseMove(Point virtualPoint, FrameRect bounds)
    {
        (int nx, int ny) = Normalize.ToAbsolute(virtualPoint, bounds);

        return new SendInputInterop.INPUT
        {
            type = SendInputInterop.INPUT_MOUSE,
            u = new SendInputInterop.INPUTUNION
            {
                mi = new SendInputInterop.MOUSEINPUT
                {
                    dx = nx,
                    dy = ny,
                    dwFlags = SendInputInterop.MOUSEEVENTF_MOVE
                            | SendInputInterop.MOUSEEVENTF_ABSOLUTE
                            | SendInputInterop.MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };
    }

    private static SendInputInterop.INPUT Key(ushort vk, bool up) => new()
    {
        type = SendInputInterop.INPUT_KEYBOARD,
        u = new SendInputInterop.INPUTUNION
        {
            ki = new SendInputInterop.KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? SendInputInterop.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static SendInputInterop.INPUT Button(MouseButton button, bool up)
    {
        (uint flag, uint data) = button switch
        {
            MouseButton.Left => (up ? SendInputInterop.MOUSEEVENTF_LEFTUP : SendInputInterop.MOUSEEVENTF_LEFTDOWN, 0u),
            MouseButton.Right => (up ? SendInputInterop.MOUSEEVENTF_RIGHTUP : SendInputInterop.MOUSEEVENTF_RIGHTDOWN, 0u),
            MouseButton.Middle => (up ? SendInputInterop.MOUSEEVENTF_MIDDLEUP : SendInputInterop.MOUSEEVENTF_MIDDLEDOWN, 0u),
            MouseButton.X1 => (up ? SendInputInterop.MOUSEEVENTF_XUP : SendInputInterop.MOUSEEVENTF_XDOWN, SendInputInterop.XBUTTON1),
            MouseButton.X2 => (up ? SendInputInterop.MOUSEEVENTF_XUP : SendInputInterop.MOUSEEVENTF_XDOWN, SendInputInterop.XBUTTON2),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        return new SendInputInterop.INPUT
        {
            type = SendInputInterop.INPUT_MOUSE,
            u = new SendInputInterop.INPUTUNION
            {
                mi = new SendInputInterop.MOUSEINPUT { dwFlags = flag, mouseData = data },
            },
        };
    }

    private static SendInputInterop.INPUT Wheel(int notches, bool horizontal) => new()
    {
        type = SendInputInterop.INPUT_MOUSE,
        u = new SendInputInterop.INPUTUNION
        {
            mi = new SendInputInterop.MOUSEINPUT
            {
                // WHEEL_DELTA is one detent. Apps divide by it, so a raw count would scroll by
                // 1/120th of a notch and appear to do nothing.
                mouseData = unchecked((uint)(notches * SendInputInterop.WHEEL_DELTA)),
                dwFlags = horizontal ? SendInputInterop.MOUSEEVENTF_HWHEEL : SendInputInterop.MOUSEEVENTF_WHEEL,
            },
        },
    };

    /// <summary>
    /// Types literal text via KEYEVENTF_UNICODE, which is layout-independent and needs no VK
    /// mapping — the caller's 'é' arrives as 'é' regardless of the active keyboard layout
    ///.
    /// </summary>
    private static IEnumerable<SendInputInterop.INPUT> Unicode(string text)
    {
        // Enumerating UTF-16 code units rather than runes is deliberate: a surrogate pair must be
        // injected as its two units in order, which is exactly what the string already holds.
        foreach (char c in text)
        {
            yield return UnicodeUnit(c, up: false);
            yield return UnicodeUnit(c, up: true);
        }
    }

    private static SendInputInterop.INPUT UnicodeUnit(char c, bool up) => new()
    {
        type = SendInputInterop.INPUT_KEYBOARD,
        u = new SendInputInterop.INPUTUNION
        {
            ki = new SendInputInterop.KEYBDINPUT
            {
                wVk = 0,           // must be 0 for KEYEVENTF_UNICODE
                wScan = c,
                dwFlags = SendInputInterop.KEYEVENTF_UNICODE | (up ? SendInputInterop.KEYEVENTF_KEYUP : 0),
            },
        },
    };
}
