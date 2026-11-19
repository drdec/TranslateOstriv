using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace OstrivTranslate.Core;

public static class AtomicFiles
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static void WriteJson<T>(string path, T value, string? protectedSource = null) => Write(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions), protectedSource);
    public static void Write(string path, byte[] bytes, string? protectedSource = null)
    {
        path = Path.GetFullPath(path);
        if (protectedSource is not null) EnsureDifferent(protectedSource, path);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(true); }
            if (!File.ReadAllBytes(temporary).AsSpan().SequenceEqual(bytes)) throw new TranslationException("VerificationFailed");
            if (protectedSource is not null) EnsureDifferent(protectedSource, path);
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N"));
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void EnsureDifferent(string source, string output)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)) throw new TranslationException("OriginalProtected");
        if (!File.Exists(output)) return;
        if (OperatingSystem.IsWindows())
        {
            using var a = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var b = File.OpenHandle(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(a, out var ai) || !GetFileInformationByHandle(b, out var bi)) throw new TranslationException("FileIdentityFailed");
            if (ai.VolumeSerialNumber == bi.VolumeSerialNumber && ai.FileIndexHigh == bi.FileIndexHigh && ai.FileIndexLow == bi.FileIndexLow) throw new TranslationException("OriginalProtected");
        }
        else
        {
            var a = new FileInfo(source).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(source);
            var b = new FileInfo(output).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(output);
            if (a == b) throw new TranslationException("OriginalProtected");
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfoNative
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative info);
}
