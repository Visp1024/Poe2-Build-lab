using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core.Items;
using PBLApp.Core.Localization;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PBLApp.ViewModels;

// ── ModLineViewModel ──────────────────────────────────────────────────────────

public sealed class ModLineViewModel
{
    public string Text        { get; }
    public string Color       { get; }
    public bool   IsSeparator { get; }
    public bool   IsTextLine  => !IsSeparator;

    public ModLineViewModel(string text, string color, bool isSeparator = false)
        { Text = text; Color = color; IsSeparator = isSeparator; }
}

// ── ItemSlotViewModel ─────────────────────────────────────────────────────────

public partial class ItemSlotViewModel : ObservableObject
{
    private readonly ItemsTabViewModel _parent;

    public string SlotName  { get; }
    public string SlotLabel { get; }

    public bool   IsEmpty    { get; private set; } = true;
    public string DisplayName { get; private set; } = "(empty)";
    public string NameColor  { get; private set; } = "#3D3F55";
    public string Rarity     { get; private set; } = "";
    public string BaseName   { get; private set; } = "";
    public int    ItemLevel  { get; private set; } = 0;

    public string TranslatedDisplayName { get; private set; } = "(empty)";
    public string TranslatedBaseName    { get; private set; } = "";
    /// <summary>Absolute path to the .webp icon for this item, or null when no icon is available.</summary>
    public string? IconPath { get; private set; }

    /// <summary>
    /// Name shown on the equipment figure / pool list. PoE convention:
    /// uniques and relics show "&lt;unique name&gt;, &lt;base&gt;" (both localised),
    /// rare/magic/normal items show only the localised base name.
    /// </summary>
    public string LeftDisplayName
    {
        get
        {
            if (IsEmpty) return DisplayName;
            if (Rarity == "UNIQUE" || Rarity == "RELIC")
            {
                var comma = DisplayName.IndexOf(',');
                if (comma > 0)
                {
                    var uniqueEn = DisplayName[..comma].Trim();
                    return GameTranslationService.Instance.Unique(uniqueEn)
                           + ", " + TranslatedBaseName;
                }
                return GameTranslationService.Instance.Unique(DisplayName);
            }
            return TranslatedBaseName;
        }
    }

    public IReadOnlyList<string> Enchants  { get; private set; } = [];
    public IReadOnlyList<string> Implicits { get; private set; } = [];
    public IReadOnlyList<string> Explicits { get; private set; } = [];

    public bool IsSelected => _parent.SelectedSlotName == SlotName;

    public IRelayCommand SelectCommand { get; }

    public ItemSlotViewModel(ItemsTabViewModel parent, string slotName, string slotLabel)
    {
        _parent       = parent;
        SlotName      = slotName;
        SlotLabel     = slotLabel;
        SelectCommand = new RelayCommand(() => _parent.SelectSlot(SlotName));
    }

    public void UpdateFrom(ItemEntry? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Name))
        {
            IsEmpty               = true;
            DisplayName           = "(empty)";
            TranslatedDisplayName = "(empty)";
            TranslatedBaseName    = "";
            NameColor             = "#3D3F55";
            Rarity                = "";
            BaseName              = "";
            ItemLevel             = 0;
            Enchants              = [];
            Implicits             = [];
            Explicits             = [];
            IconPath              = null;
        }
        else
        {
            IsEmpty               = false;
            DisplayName           = item.Name;
            TranslatedDisplayName = GameTranslationService.TItem(item.Name);
            TranslatedBaseName    = GameTranslationService.TItem(item.BaseName);
            Rarity                = item.Rarity;
            NameColor             = RarityToColor(item.Rarity);
            BaseName              = item.BaseName;
            ItemLevel             = item.ItemLevel;
            Enchants              = item.Enchants;
            Implicits             = item.Implicits;
            Explicits             = item.Explicits;
            // Uniques/relics are matched on the unique name (first segment of
            // "Bramblejack, Plate Vest"); other rarities fall back to the base.
            var unique = item.Rarity is "UNIQUE" or "RELIC" ? item.Name : null;
            IconPath              = ItemIconService.Instance.Resolve(item.BaseName, unique);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(TranslatedDisplayName));
        OnPropertyChanged(nameof(TranslatedBaseName));
        OnPropertyChanged(nameof(LeftDisplayName));
        OnPropertyChanged(nameof(NameColor));
        OnPropertyChanged(nameof(Rarity));
        OnPropertyChanged(nameof(BaseName));
        OnPropertyChanged(nameof(ItemLevel));
        OnPropertyChanged(nameof(Enchants));
        OnPropertyChanged(nameof(Implicits));
        OnPropertyChanged(nameof(Explicits));
        OnPropertyChanged(nameof(IconPath));
        OnPropertyChanged(nameof(HasIcon));
    }

    public bool HasIcon => !string.IsNullOrEmpty(IconPath);

    public void NotifySelectionChanged() => OnPropertyChanged(nameof(IsSelected));

    internal static string RarityToColor(string rarity) => rarity switch
    {
        "MAGIC"  => "#89B4FA",
        "RARE"   => "#F9E2AF",
        "UNIQUE" => "#FAB387",
        "RELIC"  => "#CBA6F7",
        _        => "#CDD6F4"
    };
}

// ── JewelSocketSlotViewModel ──────────────────────────────────────────────────

public sealed class JewelSocketSlotViewModel : ObservableObject
{
    private readonly ItemsTabViewModel _parent;

    public int    NodeId      { get; }
    public string NodeName    { get; }
    public string SlotName    { get; }
    public bool   IsEmpty     { get; }
    public string DisplayName { get; }
    public string NameColor   { get; }
    public string Rarity      { get; }
    /// <summary>Name shown in the figure — base name only for non-uniques, full name for uniques.</summary>
    public string LeftDisplayName { get; }
    public string? IconPath { get; }
    public bool   HasIcon   => !string.IsNullOrEmpty(IconPath);

    public bool   IsSelected  => _parent.SelectedSlotName == SlotName;

    public IRelayCommand SelectCommand { get; }

    public JewelSocketSlotViewModel(ItemsTabViewModel parent, JewelSocketEntry entry)
    {
        _parent  = parent;
        NodeId   = entry.NodeId;
        NodeName = entry.NodeName;
        SlotName = entry.SlotName;
        if (entry.Item is { } it && !string.IsNullOrEmpty(it.Name))
        {
            IsEmpty         = false;
            DisplayName     = it.Name;
            Rarity          = it.Rarity;
            NameColor       = ItemSlotViewModel.RarityToColor(it.Rarity);
            LeftDisplayName = BuildLeftName(it);
            var unique      = it.Rarity is "UNIQUE" or "RELIC" ? it.Name : null;
            IconPath        = ItemIconService.Instance.Resolve(it.BaseName, unique);
        }
        else
        {
            IsEmpty         = true;
            DisplayName     = string.IsNullOrEmpty(NodeName) ? "—" : NodeName;
            Rarity          = "";
            NameColor       = "#3D3F55";
            LeftDisplayName = DisplayName;
            IconPath        = null;
        }
        SelectCommand = new RelayCommand(() => _parent.SelectSlot(SlotName));
    }

    private static string BuildLeftName(ItemEntry it)
    {
        var translatedBase = GameTranslationService.TItem(it.BaseName);
        if (it.Rarity is "UNIQUE" or "RELIC")
        {
            var comma = it.Name.IndexOf(',');
            var uniqueEn = comma > 0 ? it.Name[..comma].Trim() : it.Name;
            return GameTranslationService.Instance.Unique(uniqueEn) + ", " + translatedBase;
        }
        return translatedBase;
    }

    public void NotifySelectionChanged() => OnPropertyChanged(nameof(IsSelected));
}

// ── ItemPoolEntryViewModel ────────────────────────────────────────────────────

public sealed class ItemPoolEntryViewModel : ObservableObject
{
    private readonly ItemsTabViewModel _parent;

    public int    ItemId      { get; }
    public string Name        { get; }
    public string BaseName    { get; }
    public string Rarity      { get; }
    public int    ItemLevel   { get; }
    public string PrimarySlot { get; }
    public string EquippedSlot { get; }
    public string NameColor   { get; }
    /// <summary>Pool-list compact display (figure overlay): base for non-uniques, "unique, base" for uniques.</summary>
    public string LeftDisplayName { get; }
    /// <summary>Pool-list top line: localised unique name (empty for non-uniques — top TextBlock hides).</summary>
    public string PoolTopName  { get; }
    /// <summary>Pool-list bottom line: localised base name (always present).</summary>
    public string PoolBaseName { get; }
    public bool   HasTopName   => !string.IsNullOrEmpty(PoolTopName);
    public string? IconPath    { get; }
    public bool   HasIcon      => !string.IsNullOrEmpty(IconPath);

    public bool   IsEquipped  => !string.IsNullOrEmpty(EquippedSlot);
    public bool   IsSelected  => _parent.SelectedPoolItemId == ItemId;

    public IRelayCommand SelectCommand { get; }
    public IRelayCommand DeleteCommand { get; }

    public ItemPoolEntryViewModel(ItemsTabViewModel parent, ItemPoolEntry entry)
    {
        _parent      = parent;
        ItemId       = entry.Id;
        Name         = entry.Name;
        BaseName     = entry.BaseName;
        Rarity       = entry.Rarity;
        ItemLevel    = entry.ItemLevel;
        PrimarySlot  = entry.PrimarySlot;
        EquippedSlot = entry.EquippedSlot;
        NameColor    = ItemSlotViewModel.RarityToColor(entry.Rarity);
        PoolBaseName = GameTranslationService.TItem(entry.BaseName);
        string? uniqueForIcon = null;
        if (entry.Rarity is "UNIQUE" or "RELIC")
        {
            PoolTopName     = GameTranslationService.Instance.Unique(SplitUnique(entry.Name));
            LeftDisplayName = PoolTopName + ", " + PoolBaseName;
            uniqueForIcon   = entry.Name;
        }
        else
        {
            PoolTopName     = "";
            LeftDisplayName = PoolBaseName;
        }
        IconPath = ItemIconService.Instance.Resolve(entry.BaseName, uniqueForIcon);
        SelectCommand = new RelayCommand(() => _parent.SelectPoolItem(ItemId));
        DeleteCommand = new RelayCommand(() => _parent.DeletePoolItem(ItemId));
    }

    private static string SplitUnique(string fullName)
    {
        var comma = fullName.IndexOf(',');
        return comma > 0 ? fullName[..comma].Trim() : fullName;
    }

    public void NotifySelectionChanged() => OnPropertyChanged(nameof(IsSelected));
}

// ── ItemsTabViewModel ─────────────────────────────────────────────────────────

public partial class ItemsTabViewModel : ViewModelBase
{
    private readonly LuaHost    _host;
    private readonly BuildModel _build;
    private readonly Action?    _onStatsChanged;
    private readonly List<ItemSlotViewModel> _allSlots;

    // ── Slot selection ──────────────────────────────────────────────────────

    [ObservableProperty] private string _selectedSlotName = "";

    // Named slot properties for direct AXAML binding
    public ItemSlotViewModel Helmet      { get; }
    public ItemSlotViewModel BodyArmour  { get; }
    public ItemSlotViewModel Weapon1     { get; }
    public ItemSlotViewModel Weapon2     { get; }
    public ItemSlotViewModel Weapon1Swap { get; }
    public ItemSlotViewModel Weapon2Swap { get; }
    public ItemSlotViewModel Ring1      { get; }
    public ItemSlotViewModel Ring2      { get; }
    public ItemSlotViewModel Ring3      { get; }
    public ItemSlotViewModel Gloves     { get; }
    public ItemSlotViewModel Boots      { get; }
    public ItemSlotViewModel Belt       { get; }
    public ItemSlotViewModel Amulet     { get; }
    public ItemSlotViewModel Flask1     { get; }
    public ItemSlotViewModel Flask2     { get; }
    public ItemSlotViewModel Charm1     { get; }
    public ItemSlotViewModel Charm2     { get; }
    public ItemSlotViewModel Charm3     { get; }
    public ItemSlotViewModel Arm1       { get; }
    public ItemSlotViewModel Arm2       { get; }
    public ItemSlotViewModel Leg1       { get; }
    public ItemSlotViewModel Leg2       { get; }

    // ── Slot helper ─────────────────────────────────────────────────────────

    private ItemSlotViewModel? SelectedSlot => _allSlots.FirstOrDefault(s => s.SlotName == SelectedSlotName);

    // ── Item pool ───────────────────────────────────────────────────────────

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredPool))]
    private string _poolSearchText = "";

    [ObservableProperty] private int _selectedPoolItemId = -1;

    public ObservableCollection<ItemPoolEntryViewModel> ItemPool { get; } = [];

    // Pool list view: hide items that are already equipped (they live in their slot).
    public IEnumerable<ItemPoolEntryViewModel> FilteredPool
    {
        get
        {
            IEnumerable<ItemPoolEntryViewModel> q = ItemPool.Where(e => !e.IsEquipped);
            if (!string.IsNullOrWhiteSpace(PoolSearchText))
                q = q.Where(e =>
                    e.Name.Contains(PoolSearchText, StringComparison.OrdinalIgnoreCase) ||
                    e.BaseName.Contains(PoolSearchText, StringComparison.OrdinalIgnoreCase));
            return q;
        }
    }

    public ItemPoolEntryViewModel? SelectedPoolItem =>
        ItemPool.FirstOrDefault(e => e.ItemId == SelectedPoolItemId);

    // Pool item right-panel state
    public bool   HasSelectedPoolItem  => SelectedPoolItem is not null;
    public string PoolItemName         => SelectedPoolItem?.Name         ?? "";
    public string PoolItemBaseName     => SelectedPoolItem?.BaseName     ?? "";
    public string PoolItemRarity       => SelectedPoolItem?.Rarity       ?? "";
    public string PoolItemNameColor    => SelectedPoolItem?.NameColor    ?? "#CDD6F4";
    public int    PoolItemLevel        => SelectedPoolItem?.ItemLevel    ?? 0;
    public bool   PoolItemIsEquipped   => SelectedPoolItem?.IsEquipped   == true;
    public string PoolItemEquippedSlot => SelectedPoolItem?.EquippedSlot ?? "";

    // Slots this item type can go into
    public ObservableCollection<string> CompatibleSlots { get; } = [];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEquipPoolItem))]
    private string _selectedEquipSlot = "";
    public bool CanEquipPoolItem => SelectedPoolItem is not null && !string.IsNullOrEmpty(SelectedEquipSlot);

    // PoolItemMods removed — mods are shown in the inline editor

    // ── Import panel ────────────────────────────────────────────────────────

    [ObservableProperty] private bool   _isImportPanelOpen = false;
    [ObservableProperty] private string _importItemText    = "";
    [ObservableProperty] private string _importError       = "";

    // ── Item editor ─────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorOpen), nameof(IsEquipStripVisible),
                              nameof(IsTooltipMode), nameof(IsEditMode), nameof(IsRightPaneEmpty))]
    private ItemEditorViewModel? _itemEditor;

    /// <summary>PoB-styled display tooltip for the currently selected item.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTooltipMode), nameof(IsRightPaneEmpty))]
    private ItemTooltipViewModel? _itemTooltip;

    public bool IsEditorOpen => ItemEditor is not null;
    /// <summary>Right pane shows the styled display tooltip (read-only).</summary>
    public bool IsTooltipMode => ItemTooltip is not null && ItemEditor is null;
    /// <summary>Right pane shows the editor (mods, base, rarity).</summary>
    public bool IsEditMode    => ItemEditor is not null;
    /// <summary>Right pane has no item selected — show the placeholder + New Item button.</summary>
    public bool IsRightPaneEmpty => !IsEditorOpen && !IsTooltipMode;

    // ── Weapon swap (Weapon Set I / II) ─────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeaponSet1Active), nameof(IsWeaponSet2Active))]
    private bool _isWeaponSwapActive = false;

    public bool IsWeaponSet1Active => !IsWeaponSwapActive;
    public bool IsWeaponSet2Active =>  IsWeaponSwapActive;

    public RelayCommand SelectWeaponSet1Command { get; private set; } = null!;
    public RelayCommand SelectWeaponSet2Command { get; private set; } = null!;

    // ── Jewel sockets ───────────────────────────────────────────────────────

    public ObservableCollection<JewelSocketSlotViewModel> JewelSlots { get; } = [];

    // ── Extra slots (Ring 3 / Arm / Leg) — only visible if any non-empty ────
    public bool HasExtraSlots =>
        !Ring3.IsEmpty || !Arm1.IsEmpty || !Arm2.IsEmpty || !Leg1.IsEmpty || !Leg2.IsEmpty;

    // Commands
    public RelayCommand OpenImportPanelCommand   { get; }
    public RelayCommand CloseImportPanelCommand  { get; }
    public RelayCommand ConfirmImportCommand     { get; }
    public RelayCommand EquipPoolItemCommand     { get; }
    public RelayCommand UnequipPoolItemCommand   { get; }
    public RelayCommand OpenNewItemEditorCommand { get; }
    /// <summary>Switches the right pane from tooltip to editor for the currently selected item.</summary>
    public RelayCommand EditCurrentItemCommand { get; private set; } = null!;
    /// <summary>Cancels editing, returns to the styled tooltip view.</summary>
    public RelayCommand CancelEditCommand     { get; private set; } = null!;

    public bool IsEquipStripVisible => IsEditorOpen && SelectedPoolItemId >= 0;

    // ── Constructor ─────────────────────────────────────────────────────────

    public ItemsTabViewModel(LuaHost host, BuildModel build, Action? onStatsChanged = null)
    {
        _host           = host;
        _build          = build;
        _onStatsChanged = onStatsChanged;

        Helmet     = new ItemSlotViewModel(this, "Helmet",      "Helmet");
        BodyArmour = new ItemSlotViewModel(this, "Body Armour", "Body Armour");
        Weapon1     = new ItemSlotViewModel(this, "Weapon 1",      "Weapon 1");
        Weapon2     = new ItemSlotViewModel(this, "Weapon 2",      "Weapon 2");
        Weapon1Swap = new ItemSlotViewModel(this, "Weapon 1 Swap", "Weapon 1 Swap");
        Weapon2Swap = new ItemSlotViewModel(this, "Weapon 2 Swap", "Weapon 2 Swap");
        Ring1      = new ItemSlotViewModel(this, "Ring 1",      "Ring 1");
        Ring2      = new ItemSlotViewModel(this, "Ring 2",      "Ring 2");
        Ring3      = new ItemSlotViewModel(this, "Ring 3",      "Ring 3");
        Gloves     = new ItemSlotViewModel(this, "Gloves",      "Gloves");
        Boots      = new ItemSlotViewModel(this, "Boots",       "Boots");
        Belt       = new ItemSlotViewModel(this, "Belt",        "Belt");
        Amulet     = new ItemSlotViewModel(this, "Amulet",      "Amulet");
        Flask1     = new ItemSlotViewModel(this, "Flask 1",     "Flask 1");
        Flask2     = new ItemSlotViewModel(this, "Flask 2",     "Flask 2");
        Charm1     = new ItemSlotViewModel(this, "Charm 1",     "Charm 1");
        Charm2     = new ItemSlotViewModel(this, "Charm 2",     "Charm 2");
        Charm3     = new ItemSlotViewModel(this, "Charm 3",     "Charm 3");
        Arm1       = new ItemSlotViewModel(this, "Arm 1",       "Arm 1");
        Arm2       = new ItemSlotViewModel(this, "Arm 2",       "Arm 2");
        Leg1       = new ItemSlotViewModel(this, "Leg 1",       "Leg 1");
        Leg2       = new ItemSlotViewModel(this, "Leg 2",       "Leg 2");

        _allSlots = [Helmet, BodyArmour, Weapon1, Weapon2, Weapon1Swap, Weapon2Swap,
                     Ring1, Ring2, Ring3,
                     Gloves, Boots, Belt, Amulet, Flask1, Flask2,
                     Charm1, Charm2, Charm3, Arm1, Arm2, Leg1, Leg2];

        SelectWeaponSet1Command = new RelayCommand(() => SetWeaponSet(false), () => IsWeaponSwapActive);
        SelectWeaponSet2Command = new RelayCommand(() => SetWeaponSet(true),  () => !IsWeaponSwapActive);

        OpenImportPanelCommand   = new RelayCommand(() => { IsImportPanelOpen = true; ImportError = ""; });
        CloseImportPanelCommand  = new RelayCommand(() => { IsImportPanelOpen = false; ImportItemText = ""; ImportError = ""; });
        ConfirmImportCommand     = new RelayCommand(ConfirmImport, () => !string.IsNullOrWhiteSpace(ImportItemText));
        EquipPoolItemCommand     = new RelayCommand(EquipPoolItem,   () => CanEquipPoolItem);
        UnequipPoolItemCommand   = new RelayCommand(UnequipPoolItem, () => PoolItemIsEquipped);
        OpenNewItemEditorCommand = new RelayCommand(OpenEditorForNew);
        EditCurrentItemCommand   = new RelayCommand(EditCurrentItem, () => IsTooltipMode);
        CancelEditCommand        = new RelayCommand(CancelEdit,      () => IsEditMode);

        LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();
        Refresh();
    }

    // ── Slot selection logic ────────────────────────────────────────────────

    public void SelectSlot(string slotName)
    {
        SelectedPoolItemId = -1;
        SelectedSlotName   = slotName;   // triggers OnSelectedSlotNameChanged
    }

    partial void OnSelectedSlotNameChanged(string slotName)
    {
        foreach (var slot in _allSlots)
            slot.NotifySelectionChanged();
        foreach (var js in JewelSlots)
            js.NotifySelectionChanged();
        AutoOpenEditorForSlot(slotName);
    }

    private void AutoOpenEditorForSlot(string slotName)
    {
        // Right pane defaults to the styled display tooltip; user clicks Edit to mutate.
        ItemEditor  = null;
        ItemTooltip = null;
        if (string.IsNullOrEmpty(slotName)) return;
        var poolEntry = ItemPool.FirstOrDefault(e => e.EquippedSlot == slotName);
        if (poolEntry is null) return;   // empty slot — nothing to show until user picks an item
        LoadDisplayTooltip(poolEntry.ItemId, slotName);
    }

    private void LoadDisplayTooltip(int itemId, string? slotName)
    {
        try
        {
            var lines = _host.GetItemTooltipLines(itemId, slotName);
            var vm    = new ItemTooltipViewModel();
            vm.Load(lines);
            ItemTooltip = vm;
        }
        catch
        {
            ItemTooltip = null;
        }
        EditCurrentItemCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    // ── Pool selection logic ────────────────────────────────────────────────

    public void SelectPoolItem(int itemId)
    {
        SelectedSlotName   = "";
        SelectedPoolItemId = itemId;     // triggers OnSelectedPoolItemIdChanged
    }

    partial void OnSelectedPoolItemIdChanged(int itemId)
    {
        foreach (var e in ItemPool)
            e.NotifySelectionChanged();
        RebuildCompatibleSlots();
        NotifyPoolEquipStrip();
        if (itemId >= 0) AutoOpenEditorForPoolItem(itemId);
        else             ItemEditor = null;
    }

    private void AutoOpenEditorForPoolItem(int itemId)
    {
        ItemEditor  = null;
        ItemTooltip = null;
        var entry = ItemPool.FirstOrDefault(e => e.ItemId == itemId);
        if (entry is null) return;
        LoadDisplayTooltip(itemId, slotName: null);
    }

    /// <summary>Open the editor for whatever item is currently displayed in the tooltip.</summary>
    private void EditCurrentItem()
    {
        var itemId = ResolveSelectedItemId();
        if (itemId is null) return;
        var raw    = _host.GetItemRawText(itemId.Value);
        var editor = new ItemEditorViewModel(_host, OnEditorClose, itemId.Value, raw);
        editor.SetDeleteAction(() => DeleteItemFromEditor(itemId.Value));
        ItemEditor = editor;
        EditCurrentItemCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    private void CancelEdit()
    {
        ItemEditor = null;
        // Reload the tooltip for whatever is selected.
        if (!string.IsNullOrEmpty(SelectedSlotName))      AutoOpenEditorForSlot(SelectedSlotName);
        else if (SelectedPoolItemId >= 0)                 AutoOpenEditorForPoolItem(SelectedPoolItemId);
        EditCurrentItemCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    private int? ResolveSelectedItemId()
    {
        if (!string.IsNullOrEmpty(SelectedSlotName))
        {
            var poolEntry = ItemPool.FirstOrDefault(e => e.EquippedSlot == SelectedSlotName);
            if (poolEntry is not null) return poolEntry.ItemId;
        }
        if (SelectedPoolItemId >= 0) return SelectedPoolItemId;
        return null;
    }

    private void DeleteItemFromEditor(int itemId)
    {
        ItemEditor         = null;
        SelectedSlotName   = "";
        SelectedPoolItemId = -1;
        _host.DeleteItemFromPool(itemId);
        _build.Refresh();
        Refresh();
        _onStatsChanged?.Invoke();
    }

    private void NotifyPoolEquipStrip()
    {
        OnPropertyChanged(nameof(SelectedPoolItem));
        OnPropertyChanged(nameof(HasSelectedPoolItem));
        OnPropertyChanged(nameof(PoolItemName));
        OnPropertyChanged(nameof(PoolItemBaseName));
        OnPropertyChanged(nameof(PoolItemRarity));
        OnPropertyChanged(nameof(PoolItemNameColor));
        OnPropertyChanged(nameof(PoolItemLevel));
        OnPropertyChanged(nameof(PoolItemIsEquipped));
        OnPropertyChanged(nameof(PoolItemEquippedSlot));
        OnPropertyChanged(nameof(CanEquipPoolItem));
        OnPropertyChanged(nameof(IsEquipStripVisible));
        EquipPoolItemCommand.NotifyCanExecuteChanged();
        UnequipPoolItemCommand.NotifyCanExecuteChanged();
    }

    private void RebuildCompatibleSlots()
    {
        CompatibleSlots.Clear();
        SelectedEquipSlot = "";
        var entry = SelectedPoolItem;
        if (entry is null) return;

        foreach (var s in GetCompatibleSlots(EffectivePrimarySlot(entry)))
            CompatibleSlots.Add(s);

        if (CompatibleSlots.Count > 0)
            SelectedEquipSlot = CompatibleSlots[0];
    }

    /// <summary>For a pool item id, returns the set of slot names it can be equipped into. Empty if unknown.</summary>
    public HashSet<string> CompatibleSlotsForPool(int itemId)
    {
        var item = ItemPool.FirstOrDefault(p => p.ItemId == itemId);
        if (item is null) return new HashSet<string>(StringComparer.Ordinal);
        return new HashSet<string>(GetCompatibleSlots(EffectivePrimarySlot(item)), StringComparer.Ordinal);
    }

    /// <summary>For a slot containing an item, returns OTHER slots the item could swap into.</summary>
    public HashSet<string> CompatibleSlotsForSlot(string slotName)
    {
        var item = ItemPool.FirstOrDefault(p => p.EquippedSlot == slotName);
        if (item is null) return new HashSet<string>(StringComparer.Ordinal);
        var set = new HashSet<string>(GetCompatibleSlots(EffectivePrimarySlot(item)), StringComparer.Ordinal);
        set.Remove(slotName);   // don't list the source slot itself
        return set;
    }

    /// <summary>Refines PoB's GetPrimarySlot for cases where it lumps differently-typed items
    /// into the same primarySlot (Life vs Mana flasks both → "Flask 1"; charms come back as
    /// the bare "Charm" type instead of an indexed slot).</summary>
    private static string EffectivePrimarySlot(ItemPoolEntryViewModel item)
    {
        var ps = item.PrimarySlot ?? "";
        var bn = (item.BaseName  ?? "").ToLowerInvariant();
        if (ps.StartsWith("Flask"))
        {
            // PoE2 flask slots are typed: Flask 1 = Life, Flask 2 = Mana.
            if (bn.Contains("mana flask")) return "Flask 2";
            return "Flask 1";
        }
        if (ps == "Charm") return "Charm 1";   // GetCompatibleSlots("Charm 1") covers 1/2/3
        return ps;
    }

    private IEnumerable<string> GetCompatibleSlots(string primarySlot) => primarySlot switch
    {
        "Weapon 1" => ["Weapon 1", "Weapon 2"],
        "Weapon 2" => ["Weapon 2", "Weapon 1"],
        "Ring 1"   => ["Ring 1", "Ring 2", "Ring 3"],
        "Ring 2"   => ["Ring 1", "Ring 2", "Ring 3"],
        // PoE2 flask slots are typed by primarySlot — Life Flask → Flask 1 only,
        // Mana Flask → Flask 2 only. Treat them as exclusive.
        "Flask 1"  => ["Flask 1"],
        "Flask 2"  => ["Flask 2"],
        "Charm 1"  => ["Charm 1", "Charm 2", "Charm 3"],
        "Charm 2"  => ["Charm 1", "Charm 2", "Charm 3"],
        "Charm 3"  => ["Charm 1", "Charm 2", "Charm 3"],
        // Jewels: primarySlot is the generic "Jewel" type — map it to all real
        // tree socket slot names ("Jewel <nodeId>") so the Equip ComboBox lists them.
        "Jewel"    => JewelSlots.Select(j => j.SlotName),
        ""         => [],
        _          => [primarySlot]
    };

    private void EquipPoolItem()
    {
        var entry = SelectedPoolItem;
        if (entry is null || string.IsNullOrEmpty(SelectedEquipSlot)) return;
        _host.EquipItemToSlot(entry.ItemId, SelectedEquipSlot);
        _build.Refresh();
        Refresh();
        _onStatsChanged?.Invoke();
    }

    private void UnequipPoolItem()
    {
        var entry = SelectedPoolItem;
        if (entry is null || string.IsNullOrEmpty(entry.EquippedSlot)) return;
        _host.UnequipItem(entry.EquippedSlot);
        _build.Refresh();
        Refresh();
        _onStatsChanged?.Invoke();
    }

    public void DeletePoolItem(int itemId)
        => DeleteItemFromEditor(itemId);

    /// <summary>Drag-and-drop entry point: equip a specific pool item into a slot. Returns true on success.</summary>
    public bool DragEquipPoolToSlot(int poolItemId, string slotName)
    {
        if (poolItemId <= 0 || string.IsNullOrEmpty(slotName)) return false;
        try
        {
            _host.EquipItemToSlot(poolItemId, slotName);
            _build.Refresh();
            Refresh();
            _onStatsChanged?.Invoke();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Drag-and-drop entry point: unequip the item currently in a slot.</summary>
    public bool DragUnequipSlot(string slotName)
    {
        if (string.IsNullOrEmpty(slotName)) return false;
        try
        {
            _host.UnequipItem(slotName);
            _build.Refresh();
            Refresh();
            _onStatsChanged?.Invoke();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Drag-and-drop entry point: move the item from one slot into another (swap-aware).</summary>
    public bool DragSwapSlots(string fromSlot, string toSlot)
    {
        if (string.IsNullOrEmpty(fromSlot) || string.IsNullOrEmpty(toSlot) || fromSlot == toSlot) return false;
        // Find the pool item currently sitting in fromSlot, then equip it into toSlot.
        var entry = ItemPool.FirstOrDefault(p => p.EquippedSlot == fromSlot);
        if (entry is null) return false;
        return DragEquipPoolToSlot(entry.ItemId, toSlot);
    }

    // ── Import logic ────────────────────────────────────────────────────────

    partial void OnImportItemTextChanged(string _) =>
        ConfirmImportCommand.NotifyCanExecuteChanged();

    private void ConfirmImport()
    {
        if (string.IsNullOrWhiteSpace(ImportItemText)) return;
        var ok = _host.ImportItemFromText(ImportItemText);
        if (ok)
        {
            IsImportPanelOpen = false;
            ImportItemText    = "";
            ImportError       = "";
            _build.Refresh();
            Refresh();
            _onStatsChanged?.Invoke();
        }
        else
        {
            ImportError = "Could not parse item — check format (Rarity: ... / item text)";
        }
    }

    // ── Full refresh ────────────────────────────────────────────────────────

    public void Refresh()
    {
        var items = _host.GetEquippedItems();
        foreach (var slot in _allSlots)
        {
            items.TryGetValue(slot.SlotName, out var item);
            slot.UpdateFrom(item);
        }

        IsWeaponSwapActive = _host.IsWeaponSwapActive();

        var sockets = _host.GetJewelSockets();
        JewelSlots.Clear();
        // Show sockets that are either allocated on the tree or already hold an item.
        // Avoids clutter from dozens of unused sockets while still letting the user
        // equip jewels into reachable sockets.
        foreach (var s in sockets.Where(s => s.IsAllocated || s.Item is not null))
            JewelSlots.Add(new JewelSocketSlotViewModel(this, s));

        OnPropertyChanged(nameof(HasExtraSlots));
        SelectWeaponSet1Command.NotifyCanExecuteChanged();
        SelectWeaponSet2Command.NotifyCanExecuteChanged();

        RefreshPool();

        // Re-load the display tooltip after any state change (equip, unequip,
        // mod edit, weapon-set swap) so it reflects the current item + delta.
        if (ItemEditor is null)
        {
            if (!string.IsNullOrEmpty(SelectedSlotName))      AutoOpenEditorForSlot(SelectedSlotName);
            else if (SelectedPoolItemId >= 0)                 AutoOpenEditorForPoolItem(SelectedPoolItemId);
        }
    }

    private void SetWeaponSet(bool useSecondary)
    {
        if (IsWeaponSwapActive == useSecondary) return;
        _host.SetWeaponSwapActive(useSecondary);
        _build.Refresh();
        Refresh();
        _onStatsChanged?.Invoke();
    }

    private void RefreshPool()
    {
        var prevId = SelectedPoolItemId;

        ItemPool.Clear();
        var poolEntries = _host.GetItemPool();
        // Keep ALL items in the underlying ItemPool collection (AutoOpenEditorForSlot
        // looks up the equipped pool entry by slot to open the editor). The visible
        // list uses FilteredPool which excludes equipped items.
        foreach (var e in poolEntries)
            ItemPool.Add(new ItemPoolEntryViewModel(this, e));

        OnPropertyChanged(nameof(FilteredPool));

        // Restore selection state if item still exists
        if (prevId >= 0 && ItemPool.Any(e => e.ItemId == prevId))
        {
            foreach (var e in ItemPool) e.NotifySelectionChanged();
            RebuildCompatibleSlots();
            NotifyPoolEquipStrip();
        }
        else if (prevId >= 0)
        {
            SelectedPoolItemId = -1;
        }
    }

    // ── Item editor logic ────────────────────────────────────────────────────

    private void OpenEditorForNew()
    {
        IsImportPanelOpen  = false;
        SelectedSlotName   = "";
        SelectedPoolItemId = -1;
        ItemEditor = new ItemEditorViewModel(_host, OnEditorClose);
    }

    private void OnEditorClose(ItemEditorViewModel editor, bool saved)
    {
        var editId   = editor.EditingItemId;
        var prevSlot = SelectedSlotName;
        var prevPool = SelectedPoolItemId;

        ItemEditor         = null;
        SelectedSlotName   = "";
        SelectedPoolItemId = -1;

        if (saved)
        {
            _build.Refresh();
            Refresh();
            _onStatsChanged?.Invoke();
        }

        // Restore the previous selection so the right pane returns to the
        // styled display tooltip instead of the empty placeholder.
        if (editId.HasValue)
            SelectedPoolItemId = editId.Value;       // triggers AutoOpenEditorForPoolItem → tooltip
        else if (!string.IsNullOrEmpty(prevSlot))
            SelectedSlotName = prevSlot;             // triggers AutoOpenEditorForSlot → tooltip
        else if (prevPool >= 0)
            SelectedPoolItemId = prevPool;
    }
}
