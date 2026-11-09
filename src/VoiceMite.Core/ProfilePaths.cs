namespace VoiceMite.Core;

/// <summary>Reuses an existing profile without copying, merging or deleting encrypted credentials.</summary>
public static class ProfilePaths
{
    public static string Resolve(string localApplicationData, bool demo = false)
    {
        var suffix = demo ? "-Demo" : "";
        var current = Path.Combine(localApplicationData, "VoiceMite" + suffix);
        var legacy = Path.Combine(localApplicationData, "VoiceInk" + suffix);
        if (Directory.Exists(current)) return current;
        return Directory.Exists(legacy) ? legacy : current;
    }
}
