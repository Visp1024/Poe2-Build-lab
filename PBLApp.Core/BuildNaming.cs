using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PBLApp.ViewModels;

/// <summary>Derives a human-readable build name from a PoB build XML, matching the
/// "<c>Class - Ascendancy (Llvl)</c>" scheme used by the existing build files.</summary>
public static class BuildNaming
{
    // The <Build ...> tag is always near the top of PoB XMLs; pull attributes via
    // regex rather than spinning up an XmlReader.
    private static readonly Regex ClassNameRe       = new("className=\"([^\"]*)\"",       RegexOptions.Compiled);
    private static readonly Regex AscendClassNameRe = new("ascendClassName=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex LevelRe           = new(" level=\"(\\d+)\"",            RegexOptions.Compiled);

    public const string Fallback = "Imported Build";

    /// <summary>Returns "<c>Class - Ascendancy (Llvl)</c>" (omitting empty parts),
    /// or <see cref="Fallback"/> when no class can be read from the XML.</summary>
    public static string FromXml(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return Fallback;

        var head = xml.Length > 1024 ? xml[..1024] : xml;

        var mc = ClassNameRe.Match(head);
        if (!mc.Success || string.IsNullOrEmpty(mc.Groups[1].Value)) return Fallback;
        var className = mc.Groups[1].Value;

        var ma = AscendClassNameRe.Match(head);
        var ascend = ma.Success ? ma.Groups[1].Value : "";

        var name = string.IsNullOrEmpty(ascend) || ascend == className
            ? className
            : $"{className} - {ascend}";

        var ml = LevelRe.Match(head);
        if (ml.Success) name += $" (L{ml.Groups[1].Value})";

        return name;
    }

    public enum RenameResult { Ok, NoChange, Empty, Invalid, Exists }

    /// <summary>Validates <paramref name="newName"/> as a build file name and resolves
    /// the target path (same directory as <paramref name="oldPath"/>, "<c>.xml</c>"
    /// extension). On <see cref="RenameResult.Ok"/>, <paramref name="newPath"/> is the
    /// destination to <c>File.Move</c> to. <see cref="RenameResult.NoChange"/> means the
    /// name is effectively unchanged — caller should do nothing.</summary>
    public static RenameResult TryResolveRename(string oldPath, string newName, out string newPath)
    {
        newPath = "";
        newName = (newName ?? "").Trim();
        if (string.IsNullOrEmpty(newName)) return RenameResult.Empty;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return RenameResult.Invalid;

        var dir    = Path.GetDirectoryName(oldPath) ?? "";
        var target = Path.Combine(dir, newName + ".xml");

        if (string.Equals(target, oldPath, StringComparison.OrdinalIgnoreCase))
            return RenameResult.NoChange;
        if (File.Exists(target)) return RenameResult.Exists;

        newPath = target;
        return RenameResult.Ok;
    }
}
