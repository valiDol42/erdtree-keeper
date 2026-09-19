using System.Runtime.InteropServices;

namespace ErdtreeKeeper;

/// <summary>
/// Окно сообщения средствами системы.
///
/// Нужно там, где показать своё окно нельзя: библиотеки отрисовки не нашлись,
/// Avalonia упала, или программа работает установщиком и интерфейса у неё нет
/// вовсе. Системное окно поднимается всегда.
/// </summary>
internal static class Native
{
    public const uint Ok = 0x0;
    public const uint IconError = 0x10;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
