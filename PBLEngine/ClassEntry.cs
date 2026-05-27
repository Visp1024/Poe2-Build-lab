using System.Collections.Generic;

namespace PBLEngine;

public record AscendEntry(int Id, string Name);
public record ClassEntry(int Id, string Name, IReadOnlyList<AscendEntry> Ascendancies);
