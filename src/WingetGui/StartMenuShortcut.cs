using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WingetGui.Core;

namespace WingetGui;

/// <summary>
/// Notifications need a Start menu shortcut that carries the app's AppUserModelID (to be shown at all)
/// and ToastActivatorCLSID (for a click to reach the app): on this Windows 11 build the shell drops
/// notifications from an ID that only exists in the registry, which is all the Windows App SDK
/// registers for an unpackaged app (PLANNING.md, Phase 3 notes). The process uses the same fixed ID,
/// so the taskbar button and pins match the shortcut too.
/// </summary>
internal static partial class StartMenuShortcut
{
    public const string AppUserModelId = "WingetGui";

    private static readonly Guid AppUserModelProps = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private const uint IdProperty = 5;              // System.AppUserModel.ID
    private const uint ToastActivatorProperty = 26; // System.AppUserModel.ToastActivatorCLSID

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Winget GUI.lnk");

    /// <summary>Gives this process the app's fixed ID. Call before any window or notification exists.</summary>
    public static void SetProcessId() => SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

    /// <summary>
    /// Creates or refreshes the shortcut, after notifications are registered (that's when the Windows App SDK
    /// has chosen its click handler). A shortcut that points at another copy of the exe is left alone.
    /// </summary>
    public static void Ensure()
    {
        try
        {
            if (File.Exists(ShortcutPath))
            {
                // A separate, read-only object: saving through the one that loaded the file is refused (STG_E_ACCESSDENIED).
                var existing = (IShellLinkW)new ShellLink();
                ((IPersistFile)existing).Load(ShortcutPath, 0);
                var target = new StringBuilder(1024);
                existing.GetPath(target, target.Capacity, 0, 0);
                Marshal.ReleaseComObject(existing);
                if (!string.Equals(target.ToString(), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    return; // e.g. the installed copy's shortcut: same ID, so notifications still show
            }
            var link = (IShellLinkW)new ShellLink();
            link.SetPath(Environment.ProcessPath!);
            link.SetWorkingDirectory(AppContext.BaseDirectory);
            link.SetDescription("Install, update and remove apps with winget");

            var store = (IPropertyStore)link;
            var idKey = new PropertyKey(AppUserModelProps, IdProperty);
            var id = new PropVariant { vt = VT_LPWSTR, p = Marshal.StringToCoTaskMemUni(AppUserModelId) };
            try { store.SetValue(ref idKey, ref id); } finally { Marshal.FreeCoTaskMem(id.p); }

            if (NotificationActivator() is { } clsid)
            {
                var activatorKey = new PropertyKey(AppUserModelProps, ToastActivatorProperty);
                var activator = new PropVariant { vt = VT_CLSID, p = Marshal.AllocCoTaskMem(16) };
                try
                {
                    Marshal.StructureToPtr(clsid, activator.p, false);
                    store.SetValue(ref activatorKey, ref activator);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(activator.p);
                }
            }
            store.Commit();
            ((IPersistFile)link).Save(ShortcutPath, true);
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Couldn't create the Start menu shortcut, so notifications may not show: {ex.Message}");
        }
    }

    /// <summary>
    /// The COM class the Windows App SDK registered to receive notification clicks for our ID.
    /// shortcut: read from the SDK's registry entry, which isn't public API; if it moves, notifications still show but clicks won't open the app.
    /// </summary>
    private static Guid? NotificationActivator()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\AppUserModelId\{AppUserModelId}");
        return Guid.TryParse(key?.GetValue("CustomActivator") as string, out var clsid) ? clsid : null;
    }

    private const ushort VT_LPWSTR = 31;
    private const ushort VT_CLSID = 72;

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appId);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, nint data, int flags);
        void GetIDList(out nint idList);
        void SetIDList(nint idList);
        void GetDescription(nint name, int max);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(nint dir, int max);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments(nint args, int max);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation(nint path, int max, out int index);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        void Resolve(nint window, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, int mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string file, [MarshalAs(UnmanagedType.Bool)] bool remember);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        private ushort _reserved1, _reserved2, _reserved3;
        public nint p;
        private nint _p2;
    }
}
