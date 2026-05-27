using System.Collections.Generic;

namespace PBLApp.ViewModels;

public record TooltipTextSegment(string Text, string Color);

public record GemTooltipEntry(
    string Text,
    string Color,
    bool IsSep = false,
    IReadOnlyList<TooltipTextSegment>? Segments = null);
