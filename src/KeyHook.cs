using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MyTypingPet
{
    // 어느 창에서 타이핑하든 "키가 눌렸다"는 신호를 받는 전역 키보드 훅.
    // 키 코드(가상 키 번호)만 넘기고, 어떤 글자인지는 해석하지도 저장하지도 않는다.
    class KeyHook : IDisposable
    {
        public event Action<int> KeyDown;

        readonly Native.HookProc proc;   // GC 가 콜백을 수거하지 않도록 필드로 붙잡아 둔다
        IntPtr hook;

        public KeyHook()
        {
            proc = Callback;
            using (Process p = Process.GetCurrentProcess())
            using (ProcessModule m = p.MainModule)
            {
                hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, proc, Native.GetModuleHandle(m.ModuleName), 0);
            }
            if (hook == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);   // KBDLLHOOKSTRUCT 의 첫 필드
                    Action<int> handler = KeyDown;
                    if (handler != null)
                    {
                        try { handler(vkCode); } catch { /* 훅 안에서 예외가 나도 키 입력은 막지 않는다 */ }
                    }
                }
            }
            return Native.CallNextHookEx(hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(hook);
                hook = IntPtr.Zero;
            }
        }
    }
}
