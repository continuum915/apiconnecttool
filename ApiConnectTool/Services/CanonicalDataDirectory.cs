using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ApiConnectTool.Services;

internal static class CanonicalDataDirectory
{
    private const uint ShareReadWriteDelete = 1 | 2 | 4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    // Resolve existing ancestors without creating anything before ownership is acquired.
    // The directory handle resolves junctions, symbolic links and Windows short names.
    public static string Resolve(string path)
    {
        var existing = Path.GetFullPath(path);
        var suffix = new Stack<string>();
        while (!Directory.Exists(existing))
        {
            suffix.Push(Path.GetFileName(Path.TrimEndingDirectorySeparator(existing)));
            existing = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(existing))
                ?? throw new DirectoryNotFoundException(path);
        }
        using var handle = CreateFile(existing, 0, ShareReadWriteDelete, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastWin32Error());
        var resolved = buffer.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) resolved = @"\\" + resolved[8..];
        else if (resolved.StartsWith(@"\\?\", StringComparison.Ordinal)) resolved = resolved[4..];
        foreach (var segment in suffix) resolved = Path.Combine(resolved, segment);
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
