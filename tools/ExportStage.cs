namespace Mortz.Tools;

public static class ExportStage
{
    public static void Synchronize(string source, string destination, Func<string, bool> include)
    {
        SynchronizeDirectory(source, destination, include, "");
    }

    private static void SynchronizeDirectory(string source, string destination, Func<string, bool> include,
        string relative)
    {
        Directory.CreateDirectory(destination);
        HashSet<string> wanted = new(OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (string entry in Directory.EnumerateFileSystemEntries(source))
        {
            string name = Path.GetFileName(entry);
            string path = Path.Combine(relative, name);
            if (!include(path))
            {
                continue;
            }
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
            {
                throw new Exception($"export input must not be a symbolic link: {entry}");
            }
            wanted.Add(name);
            string target = Path.Combine(destination, name);
            if (Directory.Exists(entry))
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
                SynchronizeDirectory(entry, target, include, path);
            }
            else
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                if (!SameContents(entry, target))
                {
                    File.Copy(entry, target, overwrite: true);
                }
            }
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(destination))
        {
            string name = Path.GetFileName(entry);
            if (wanted.Contains(name) || IsGenerated(entry, relative, source))
            {
                continue;
            }
            if (Directory.Exists(entry))
            {
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }
        }
    }

    private static bool IsGenerated(string entry, string relative, string source)
    {
        string name = Path.GetFileName(entry);
        // These intermediates belong only to this flavor/platform/role's staging directory.
        if (Directory.Exists(entry))
        {
            return name is "bin" or "obj" || (relative == "" && name == ".godot");
        }
        if (relative == "" && name == "Mortz.Export.props")
        {
            return true;
        }
        return Path.GetExtension(name) is ".import" or ".uid" &&
               File.Exists(Path.Combine(source, Path.GetFileNameWithoutExtension(name)));
    }

    private static bool SameContents(string source, string destination)
    {
        if (!File.Exists(destination) || new FileInfo(source).Length != new FileInfo(destination).Length)
        {
            return false;
        }
        using FileStream left = File.OpenRead(source);
        using FileStream right = File.OpenRead(destination);
        Span<byte> leftBuffer = stackalloc byte[8192];
        Span<byte> rightBuffer = stackalloc byte[8192];
        int count;
        while ((count = left.Read(leftBuffer)) > 0)
        {
            right.ReadExactly(rightBuffer[..count]);
            if (!leftBuffer[..count].SequenceEqual(rightBuffer[..count]))
            {
                return false;
            }
        }
        return true;
    }
}
