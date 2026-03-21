// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Debug;

namespace Rustun.Helpers;

internal partial class NativeMethods
{
    internal static bool IsKeyDownHook(IntPtr lWord)
    {
        // The 30th bit tells what the previous key state is with 0 being the "UP" state
        // For more info see https://learn.microsoft.com/windows/win32/winmsg/keyboardproc#lparam-in
        return (lWord >> 30 & 1) == 0;
    }

    internal delegate IntPtr WinProc(IntPtr hWnd, WindowMessage Msg, IntPtr wParam, IntPtr lParam);

    [Flags]
    internal enum WindowLongIndexFlags : int
    {
        GWL_WNDPROC = -4,
    }

    internal enum WindowMessage : int
    {
        WM_GETMINMAXINFO = 0x0024,
    }

    internal static bool IsAppPackaged { get; } = GetCurrentPackageName() != null;
    internal static string? GetCurrentPackageName()
    {
        unsafe
        {
            uint packageFullNameLength = 0;

            var result = Windows.Win32.PInvoke.GetCurrentPackageFullName(&packageFullNameLength, null);

            if (result == WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER)
            {
                char* packageFullName = stackalloc char[(int)packageFullNameLength];

                result = Windows.Win32.PInvoke.GetCurrentPackageFullName(&packageFullNameLength, packageFullName);

                if (result == 0) // S_OK or ERROR_SUCCESS
                {
                    return new string(packageFullName, 0, (int)packageFullNameLength);
                }
            }
        }

        return null;
    }

    internal static string GetErrorMessage(int errorCode)
    {
        unsafe
        {
            FORMAT_MESSAGE_OPTIONS options = (FORMAT_MESSAGE_OPTIONS.FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_OPTIONS.FORMAT_MESSAGE_IGNORE_INSERTS);

            char[] buffer = new char[512];
            sbyte* arguments = null;

            uint size = Windows.Win32.PInvoke.FormatMessage(options, (void*)0, (uint)errorCode, 0, buffer, (uint)buffer.Length, in arguments);
            if (size == 0)
            {
                return $"Unknown error (code {errorCode})";
            }

            string message = new string(buffer, 0, (int)size);
            return message.Trim();
        }
    }
}
