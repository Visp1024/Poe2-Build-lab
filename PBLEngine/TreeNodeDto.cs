namespace PBLEngine;

/// <summary>Ascendancy background plate from tree data: sprite name, world
/// center position and world size (uniform 1500x1500 in tree 0_5).</summary>
public record AscendancyBgDto(
    string Id,
    string Image,
    double X,
    double Y,
    double Width,
    double Height
);

public record TreeNodeDto(
    int      Id,
    double   X,
    double   Y,
    string   Type,
    string   Name,
    string[] Stats,
    int[]    LinkedIds,
    string   AscendancyName,
    string   Icon,           // "Art/2DArt/SkillIcons/passives/..."
    string   OverlayUnalloc, // frame sprite name (unallocated)
    string   OverlayAlloc,   // frame sprite name (allocated)
    string   OverlayPath,    // frame sprite name (can-allocate)
    double   JewelRadius,    // world-space radius for Socket nodes (0 = none)
    bool     IsAttribute     // true for "+5 to any attribute" nodes
);
