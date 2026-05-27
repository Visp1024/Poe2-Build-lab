#!/usr/bin/env pwsh
# One-shot migration of Catppuccin Mocha hardcoded hex literals in PBLApp views
# onto our DynamicResource token references.
#
# Replacements are textual (whole-attribute-value match: "#XXXXXX") so we don't
# accidentally rewrite C# string literals like "#FFFFFF" inside .axaml.cs files.

param(
    [string]$Root = "D:\Work\PathBuildLab\PBLApp\Views"
)

# Hex literal -> DynamicResource brush name.
# Keys are lowercased for case-insensitive matching.
$Map = [ordered]@{
    # Surfaces (lighter Catppuccin variants collapse into our 5-step scale)
    '#11111b' = 'BgBaseBrush'
    '#181825' = 'BgMantleBrush'
    '#1e1e2e' = 'BgMantleBrush'
    '#15151f' = 'BgSurfaceBrush'
    '#1f1f30' = 'BgSurfaceBrush'
    '#191926' = 'BgSurfaceBrush'
    '#13131d' = 'BgSurfaceBrush'
    '#252537' = 'BgSurfaceBrush'
    '#313244' = 'BgRaisedBrush'
    '#2a2a40' = 'BgRaisedBrush'
    '#2a2a45' = 'BgRaisedBrush'
    '#2a2a3e' = 'BgRaisedBrush'
    '#3d3f55' = 'BgOverlayBrush'

    # Borders
    '#45475a' = 'BorderStrongBrush'

    # Text
    '#cdd6f4' = 'TextPrimaryBrush'
    '#bac2de' = 'ModImplicitBrush'
    '#a6adc8' = 'TextSecondaryBrush'
    '#585b70' = 'TextMutedBrush'
    '#6c7086' = 'TextMutedBrush'
    '#7f8fa6' = 'TextMutedBrush'

    # Attribute / stat colors (Catppuccin -> our token aliases)
    '#f38ba8' = 'AttrStrBrush'
    '#a6e3a1' = 'AttrDexBrush'
    '#89b4fa' = 'AttrIntBrush'
    '#89dceb' = 'StatEsBrush'
    '#fab387' = 'StatArmourBrush'
    '#cba6f7' = 'StatEvasionBrush'

    # Mod / rarity / brand-ish
    '#74c7ec' = 'ModEnchantBrush'
    '#f9e2af' = 'Brand400Brush'
    '#8888ff' = 'RarityMagicBrush'
}

$files = Get-ChildItem $Root -Filter *.axaml -Recurse
$total = 0
foreach ($f in $files) {
    $orig = Get-Content $f.FullName -Raw
    $text = $orig
    foreach ($k in $Map.Keys) {
        $brush = $Map[$k]
        # Match "#XXXXXX" as a quoted attribute value (case-insensitive).
        $pattern = '(?i)"' + [regex]::Escape($k) + '"'
        $text = [regex]::Replace($text, $pattern, '"{DynamicResource ' + $brush + '}"')
    }
    if ($text -ne $orig) {
        Set-Content $f.FullName -Value $text -NoNewline
        $changed = ($orig.Length - $text.Length)
        Write-Host "edited: $($f.Name) (delta $changed chars)"
        $total++
    }
}
Write-Host "---"
Write-Host "files changed: $total"
