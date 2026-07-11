namespace PBLEngine;

/// <summary>One selectable entry in the tree heat-map stat dropdown, sourced from
/// Lua <c>data.powerStatList</c>. <see cref="CombinedOffDef"/> marks the default
/// "Offence/Defence" mode (no single stat).</summary>
public record PowerStatOption(
    string? StatKey,        // e.g. "FullDPS"; null for the Offence/Defence entry
    string  Label,          // English label from powerStatList (UI translates separately)
    bool    CombinedOffDef,
    bool    IgnoreForNodes, // item-only entries (filtered out of the node heat map)
    bool    LowerIsBetter
);

/// <summary>Per-node power result. <see cref="Power"/> is the raw single-stat delta
/// (or offence in Off/Def mode) used for colouring and sorting; <see cref="PowerStr"/>
/// / <see cref="PerPointStr"/> are PoB-formatted display strings for the report.
/// <see cref="Steps"/>/<see cref="PathPower"/>/<see cref="PerPointStr"/> are null for
/// allocated and cluster nodes (no meaningful "points to take" path).</summary>
public record NodePowerEntry(
    int     Id,
    string  Name,
    string  Type,           // Normal / Notable / Keystone
    bool    Alloc,
    int?    Steps,
    double  Power,
    double? PathPower,
    double  Offence,
    double  Defence,
    string  PowerStr,
    string? PerPointStr
);

/// <summary>Raw per-node result of one power batch (unformatted deltas +
/// PoB-formatted display string).</summary>
public record PowerBatchRow(int Id, double Power, double Offence, double Defence, string PowerStr);

/// <summary>Deferred path-power result (lazy top-K phase).</summary>
public record PathPowerRow(int Id, double PathPower, string PerPointStr);

/// <summary>One candidate node for the power calc. <see cref="Steps"/> is the
/// number of points to spend to take the node (unallocated path incl. itself);
/// null for allocated / cluster / unreachable nodes.</summary>
public record PowerNodeInfo(
    int Id, string ModKey, string Name, string Type,
    bool Alloc, bool IsCluster, int? Steps);

/// <summary>Per-channel maxima used to normalise colour brightness.</summary>
public record NodePowerMax(double SingleStat, double Offence, double Defence);

/// <summary>Full heat-map result for the current stat selection.</summary>
public record NodePowerResult(
    bool OffDefMode,
    NodePowerMax Max,
    IReadOnlyList<NodePowerEntry> Entries
);
