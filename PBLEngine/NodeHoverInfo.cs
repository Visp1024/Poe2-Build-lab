namespace PBLEngine;

/// <summary>One stat diff line shown in the hover tooltip when the user
/// considers (de)allocating a tree node. PoB's <c>CompareStatList</c>
/// emits these as text with embedded colour codes; we split them into
/// typed fields so the C# renderer can lay them out with design tokens.</summary>
public record NodeStatDiff(
    string Label,        // localised stat name, e.g. "Total Life"
    string ValueText,    // formatted delta, e.g. "+25" or "-3.5%"
    bool   IsPositive,   // colour hint — green vs red
    string PercentText,  // "" or "(+5.2%)" — relative change
    string PerPointText  // "" or "[+12 per point]" when path length > 1
);

/// <summary>Full info packet for the tree's hover tooltip. <see cref="StatDiffs"/>
/// reflects "what changes if I allocate / deallocate this node". <see cref="PathStatDiffs"/>
/// shows the same totals across the whole path leading to the node (PoB convention).</summary>
public record NodeHoverInfo(
    int    NodeId,
    string Name,             // translated display name
    string Type,             // Normal / Notable / Keystone / Socket / Mastery / AscendClassStart
    string AscendancyName,
    bool   IsAllocated,
    int    PathDist,         // points to allocate (0 = already allocated)
    int    PathLength,
    string[] Mods,           // raw mod text lines (e.g. "5% increased Cold Damage")
    NodeStatDiff[] StatDiffs,        // delta from (un)allocating just this node
    NodeStatDiff[] PathStatDiffs,    // delta from (un)allocating whole path (≥ 2 nodes)
    string DiffHeader,               // "Allocating this node will give you:" etc.
    string PathDiffHeader            // ditto for the path
);
