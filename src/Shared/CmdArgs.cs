using Godot;

namespace Mortz.Shared;

/// <summary>
/// User command-line args (everything after the `++` separator, e.g.
/// `Mortz.exe --headless ++ --server --port 7778`).
/// </summary>
public static class CmdArgs
{
    private static string[] Args => OS.GetCmdlineUserArgs();

    public static bool HasFlag(string flag) => HasFlag(Args, flag);

    /// <summary>Overload taking an explicit arg list, for tests.</summary>
    public static bool HasFlag(IEnumerable<string> args, string flag)
    {
        foreach (string a in args)
        {
            if (a == flag)
                return true;
        }
        return false;
    }

    /// <summary>Value following <paramref name="flag"/>, or null.</summary>
    public static string? GetValue(string flag) => GetValue(Args, flag);

    /// <summary>Overload taking an explicit arg list, for tests.</summary>
    public static string? GetValue(IReadOnlyList<string> args, string flag)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == flag)
                return args[i + 1];
        }
        return null;
    }

    public static int GetInt(string flag, int fallback) =>
        int.TryParse(GetValue(flag), out int v) ? v : fallback;
}
