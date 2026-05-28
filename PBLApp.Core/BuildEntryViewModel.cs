using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;

namespace PBLApp.ViewModels;

public enum BuildEntryKind
{
    Build,
    Folder,
    /// <summary>Virtual "+ Create" placeholder rendered as the last card.</summary>
    Create,
}

public partial class BuildEntryViewModel : ObservableObject
{
    public BuildEntryKind Kind { get; }
    public string Name  { get; }
    public string Path  { get; }

    public bool IsFolder => Kind == BuildEntryKind.Folder;
    public bool IsBuild  => Kind == BuildEntryKind.Build;
    public bool IsCreate => Kind == BuildEntryKind.Create;

    /// <summary>Sub-folder children — kept for back-compat with the legacy TreeView path
    /// (not used by the grid view, which navigates one folder at a time).</summary>
    public ObservableCollection<BuildEntryViewModel> Children { get; } = new();

    // ── Parsed metadata (builds only) ─────────────────────────────────────
    [ObservableProperty] private string _className       = "";
    [ObservableProperty] private string _ascendClassName = "";
    [ObservableProperty] private int    _level;

    /// <summary>"Druid · Oracle · Lvl 98"-style line shown under the build name.</summary>
    public string Subtitle
    {
        get
        {
            if (Kind != BuildEntryKind.Build) return "";
            var parts = new System.Collections.Generic.List<string>(3);
            if (!string.IsNullOrEmpty(ClassName))       parts.Add(ClassName);
            if (!string.IsNullOrEmpty(AscendClassName) && AscendClassName != ClassName)
                                                        parts.Add(AscendClassName);
            if (Level > 0)                              parts.Add($"Lvl {Level}");
            return string.Join(" · ", parts);
        }
    }

    public BuildEntryViewModel(string name, string path, BuildEntryKind kind)
    {
        Kind = kind;
        Name = name;
        Path = path;

        if (kind == BuildEntryKind.Build)
            TryParseBuildMeta(path);
    }

    /// <summary>Back-compat: bool overload used by older call sites.</summary>
    public BuildEntryViewModel(string name, string path, bool isFolder)
        : this(name, path, isFolder ? BuildEntryKind.Folder : BuildEntryKind.Build) { }

    // ── Build-XML metadata reader ─────────────────────────────────────────

    // The <Build ...> tag is always on line 2 of PoB XMLs; reading the first 1 KB
    // and pulling attributes out via regex is much cheaper than spinning up an
    // XmlReader for every grid tile.
    private static readonly Regex ClassNameRe       = new("className=\"([^\"]*)\"",       RegexOptions.Compiled);
    private static readonly Regex AscendClassNameRe = new("ascendClassName=\"([^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex LevelRe           = new(" level=\"(\\d+)\"",            RegexOptions.Compiled);

    private void TryParseBuildMeta(string xmlPath)
    {
        try
        {
            if (!File.Exists(xmlPath)) return;
            using var fs = new FileStream(xmlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            var buf = new char[1024];
            int read = sr.Read(buf, 0, buf.Length);
            if (read <= 0) return;
            var head = new string(buf, 0, read);

            var mc = ClassNameRe.Match(head);
            if (mc.Success) ClassName = mc.Groups[1].Value;

            var ma = AscendClassNameRe.Match(head);
            if (ma.Success) AscendClassName = ma.Groups[1].Value;

            var ml = LevelRe.Match(head);
            if (ml.Success && int.TryParse(ml.Groups[1].Value, out var lv)) Level = lv;

            OnPropertyChanged(nameof(Subtitle));
        }
        catch { /* metadata is best-effort, never block on a malformed XML */ }
    }
}
