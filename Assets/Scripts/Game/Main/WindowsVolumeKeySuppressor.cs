#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class WindowsVolumeKeySuppressor
{
    const int GWL_WNDPROC = -4;
    const uint WM_APPCOMMAND = 0x0319;
    const ushort FAPPCOMMAND_MASK = 0xF000;
    const ushort APPCOMMAND_VOLUME_MUTE = 8;
    const ushort APPCOMMAND_VOLUME_DOWN = 9;
    const ushort APPCOMMAND_VOLUME_UP = 10;

    static IntPtr m_OldWindowProcedure;
    static WndProc m_WindowProcedure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallOnStartup()
    {
        Install();
    }

    public static void Install()
    {
        if (m_WindowProcedure != null)
            return;

        var windowHandle = GetActiveWindow();
        if (windowHandle == IntPtr.Zero)
            windowHandle = GetForegroundWindow();
        if (windowHandle == IntPtr.Zero)
            return;

        m_WindowProcedure = WindowProcedure;
        m_OldWindowProcedure = SetWindowLongPtr(windowHandle, GWL_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(m_WindowProcedure));
        Debug.Log("Windows volume key suppressor installed");
    }

    static IntPtr WindowProcedure(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_APPCOMMAND)
        {
            var rawCommand = unchecked((ushort)((long)lParam >> 16));
            var command = (ushort)(rawCommand & ~FAPPCOMMAND_MASK);
            if (command == APPCOMMAND_VOLUME_MUTE ||
                command == APPCOMMAND_VOLUME_DOWN ||
                command == APPCOMMAND_VOLUME_UP)
            {
                return new IntPtr(1);
            }
        }

        return CallWindowProc(m_OldWindowProcedure, windowHandle, message, wParam, lParam);
    }

    delegate IntPtr WndProc(IntPtr windowHandle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr GetActiveWindow();

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr64(IntPtr windowHandle, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    static extern IntPtr SetWindowLong32(IntPtr windowHandle, int index, IntPtr value);

    static IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : SetWindowLong32(windowHandle, index, value);
    }

    [DllImport("user32.dll")]
    static extern IntPtr CallWindowProc(IntPtr previousProcedure, IntPtr windowHandle, uint message,
        IntPtr wParam, IntPtr lParam);
}
#endif
