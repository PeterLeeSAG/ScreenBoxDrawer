using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
namespace ScreenBoxDrawer
{
    internal class LowLevelMouseHook
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;

        private IntPtr _hookId = IntPtr.Zero;
        private NativeMethodsLowLevel.LowLevelMouseProc? _proc;
        private readonly MainWindow _mainWindow;
        private bool _isDrawing = false;

        public LowLevelMouseHook(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
            _proc = HookCallback;
        }

        public void Start()
        {
            _hookId = SetHook(_proc!);
            if (_hookId == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                Debug.WriteLine($"Failed to set mouse hook. LastError={err}");
            }
            else
            {
                Debug.WriteLine($"Mouse hook installed. handle={_hookId}");
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethodsLowLevel.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        public void CancelDrawing()
        {
            _isDrawing = false;
        }

        private IntPtr SetHook(NativeMethodsLowLevel.LowLevelMouseProc proc)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            return NativeMethodsLowLevel.SetWindowsHookEx(WH_MOUSE_LL, proc,
                NativeMethodsLowLevel.GetModuleHandle(curModule.ModuleName), 0);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    var ms = Marshal.PtrToStructure<NativeMethodsLowLevel.MSLLHOOKSTRUCT>(lParam);

                    if (msg == WM_LBUTTONDOWN)
                    {
                        bool ctrl = (NativeMethodsLowLevel.GetAsyncKeyState(NativeMethodsLowLevel.VK_CONTROL) & 0x8000) != 0;
                        bool shift = (NativeMethodsLowLevel.GetAsyncKeyState(NativeMethodsLowLevel.VK_SHIFT) & 0x8000) != 0;
                        if (_mainWindow.IsDrawArmed || (ctrl && shift))
                        {
                            _isDrawing = _mainWindow.BeginDraw(new System.Drawing.Point(ms.pt.x, ms.pt.y));
                            if (_isDrawing)
                            {
                                // Swallow only the initial down so the underlying app does not start a selection.
                                return (IntPtr)1;
                            }
                        }
                    }

                    if (msg == WM_MOUSEMOVE)
                    {
                        if (_isDrawing)
                        {
                            _mainWindow.UpdateMouse(new System.Drawing.Point(ms.pt.x, ms.pt.y));
                        }
                        return NativeMethodsLowLevel.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                    else if (msg == WM_LBUTTONUP)
                    {
                        if (_isDrawing)
                        {
                            _isDrawing = false;
                            _mainWindow.EndDraw();
                        }
                        return NativeMethodsLowLevel.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in HookCallback: {ex}");
            }

            return NativeMethodsLowLevel.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    internal static class NativeMethodsLowLevel
    {
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const int VK_CONTROL = 0x11;
        public const int VK_SHIFT = 0x10;

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
