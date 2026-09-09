using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PBLApp.Core;
using PBLApp.Core.Import;
using PBLApp.Core.Localization;
using PBLApp.Core.Trader;
using PBLEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PBLApp.ViewModels;

/// <summary>Окраска строки статуса — вид выбирает кисть по ней.</summary>
public enum ImportStatusKind { Info, Ok, Warning, Error }

/// <summary>Режим окна: персонаж становится НОВЫМ билдом (с экрана списка) либо
/// перезаписывает УЖЕ ОТКРЫТЫЙ билд — «Обновить из игры».</summary>
public enum CharacterImportMode { NewBuild, Reimport }

/// <summary>Персонаж в списке; класс отдельно — вид красит его цветом класса.</summary>
public sealed class CharacterEntryViewModel(CharacterSummary character)
{
    public CharacterSummary Character { get; } = character;
    public string Name => Character.Name;
    public string Class => Character.Class;
    public int Level => Character.Level;
    public string League => Character.League;
    public string Meta => $"{Character.Level} · {Character.League}";
}

/// <summary>
/// Окно «Импорт персонажа»: вход в аккаунт GGG (тот же, что у «Трейдера»), список
/// персонажей аккаунта и собственно импорт. Живёт только на экране выбора билда —
/// персонаж всегда становится НОВЫМ билдом. Скачивание — <see cref="CharacterApi"/>,
/// разбор — штатный Lua-ImportTab через <see cref="LuaHost.ImportCharacter"/>.
/// </summary>
public partial class CharacterImportViewModel : ViewModelBase
{
    /// <summary>В фильтре лиг — «все лиги»; не совпадает ни с одним именем лиги.</summary>
    public const string AllLeagues = "*";

    private readonly Task<LuaHost> _hostTask;
    private readonly PoeOAuthService _oauth;
    private readonly CharacterApi _api;

    private readonly string _buildsFolder;
    private readonly Action<string>? _onImported;

    // Reimport-режим: куда писать и кого дёргать после перезаписи открытого билда.
    private readonly BuildModel? _build;
    private readonly string? _xmlPath;
    private readonly Func<Task>? _afterReimport;
    private CharacterBinding _binding = new(null, null, null);

    private IReadOnlyList<CharacterSummary> _allCharacters = [];

    /// <summary>Открыть ссылку в браузере — подменяется в тестах.</summary>
    public Action<string> OpenBrowser { get; init; } =
        url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    /// <summary>Успешный импорт в NewBuild-режиме — хост-окну пора закрыться.</summary>
    public event Action? CloseRequested;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private ImportStatusKind _statusKind = ImportStatusKind.Info;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private string _selectedLeague = AllLeagues;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private CharacterEntryViewModel? _selectedCharacter;

    // ── Что импортировать (чекбоксы ImportTab) ───────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _importPassiveTree = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private bool _importItemsAndSkills = true;

    [ObservableProperty] private bool _ignoreWeaponSwap;

    public ObservableCollection<CharacterEntryViewModel> Characters { get; } = [];
    public ObservableCollection<string> Leagues { get; } = [AllLeagues];

    public bool IsLoggedIn => _oauth.IsLoggedIn;
    public string AccountName => _oauth.AccountName ?? "";
    public string AccountInitial => AccountName is { Length: > 0 } n ? n[..1].ToUpperInvariant() : "?";
    public bool HasCharacters => Characters.Count > 0;

    public bool CanImport =>
        SelectedCharacter is not null && (ImportPassiveTree || ImportItemsAndSkills) && !IsBusy;

    /// <summary>Новый билд или перезапись открытого.</summary>
    public CharacterImportMode Mode { get; }

    public bool IsReimport => Mode == CharacterImportMode.Reimport;

    public string WindowTitle => LocalizationService.Get(
        IsReimport ? "CharImport_UpdateTitle" : "CharImport_Title");

    /// <summary>Текст кнопки действия: «Импортировать» / «Обновить билд».</summary>
    public string ActionButtonText => LocalizationService.Get(
        IsReimport ? "CharImport_UpdateButton" : "CharImport_Button");

    /// <summary>Персонаж, к которому привязан открытый билд («Билд импортирован из: Xyz»);
    /// пусто — привязки нет, пользователь выбирает персонажа сам.</summary>
    public string BoundCharacterName => _binding.CharacterName ?? "";

    public bool HasBinding => IsReimport && BoundCharacterName.Length > 0;

    public string BoundCharacterLine =>
        string.Format(LocalizationService.Get("CharImport_BoundTo"), BoundCharacterName);

    /// <summary>Реимпорт перетирает содержимое билда — предупреждаем об этом в окне.</summary>
    public bool ShowOverwriteWarning => IsReimport;

    /// <summary>Персонаж становится новым файлом билда в <paramref name="buildsFolder"/>,
    /// затем <paramref name="onImported"/> его открывает.</summary>
    public CharacterImportViewModel(Task<LuaHost> hostTask, string buildsFolder,
        Action<string> onImported, PoeOAuthService? oauth = null, CharacterApi? api = null)
    {
        Mode = CharacterImportMode.NewBuild;
        _hostTask = hostTask;
        _buildsFolder = buildsFolder;
        _onImported = onImported;
        _oauth = oauth ?? new PoeOAuthService(null);
        _api = api ?? new CharacterApi(_oauth);
        Characters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCharacters));
        if (!IsLoggedIn)
            SetStatus("CharImport_NotAuthenticated", ImportStatusKind.Warning);
    }

    /// <summary>«Обновить из игры»: персонаж перезаписывает УЖЕ ОТКРЫТЫЙ билд
    /// <paramref name="build"/> (файл <paramref name="xmlPath"/>), после чего
    /// <paramref name="afterReimport"/> обновляет вкладки страницы билда.</summary>
    public CharacterImportViewModel(Task<LuaHost> hostTask, BuildModel build, string xmlPath,
        Func<Task> afterReimport, PoeOAuthService? oauth = null, CharacterApi? api = null)
    {
        Mode = CharacterImportMode.Reimport;
        _hostTask = hostTask;
        _buildsFolder = Path.GetDirectoryName(xmlPath) ?? "";
        _build = build;
        _xmlPath = xmlPath;
        _afterReimport = afterReimport;
        _oauth = oauth ?? new PoeOAuthService(null);
        _api = api ?? new CharacterApi(_oauth);
        Characters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCharacters));
        _ = LoadBindingAsync();
        if (!IsLoggedIn)
            SetStatus("CharImport_NotAuthenticated", ImportStatusKind.Warning);
    }

    /// <summary>Читает из движка, к какому персонажу привязан открытый билд.</summary>
    private async Task LoadBindingAsync()
    {
        try
        {
            var host = await _hostTask;
            _binding = await Task.Run(host.GetCharacterBinding);
            OnPropertyChanged(nameof(BoundCharacterName));
            OnPropertyChanged(nameof(HasBinding));
            OnPropertyChanged(nameof(BoundCharacterLine));
            // Список мог загрузиться раньше привязки — перевыбираем персонажа.
            if (Characters.Count > 0 && await SelectBoundCharacterAsync())
                SetStatus("CharImport_UpdateReady", ImportStatusKind.Info);
        }
        catch
        {
            // Движок не готов — окно просто не подсветит привязку.
        }
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanImport));
    partial void OnSearchChanged(string value) => ApplyFilter();
    partial void OnSelectedLeagueChanged(string value) => ApplyFilter();

    // ── Вход ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        SetStatus("CharImport_WaitingBrowser", ImportStatusKind.Info);
        try
        {
            var ok = await _oauth.LoginAsync(OpenBrowser);
            NotifyAccount();
            if (!ok)
            {
                SetStatus("CharImport_LoginFailed", ImportStatusKind.Error);
                return;
            }
        }
        finally { IsBusy = false; }

        await LoadCharactersAsync();
    }

    [RelayCommand]
    private void Logout()
    {
        _oauth.Logout();
        _allCharacters = [];
        Characters.Clear();
        Leagues.Clear();
        Leagues.Add(AllLeagues);
        SelectedLeague = AllLeagues;
        NotifyAccount();
        SetStatus("CharImport_NotAuthenticated", ImportStatusKind.Warning);
    }

    private void NotifyAccount()
    {
        OnPropertyChanged(nameof(IsLoggedIn));
        OnPropertyChanged(nameof(AccountName));
        OnPropertyChanged(nameof(AccountInitial));
    }

    // ── Список персонажей ────────────────────────────────────────────────────

    /// <summary>Тянет список персонажей аккаунта; зовётся окном при открытии
    /// (если вход уже есть) и кнопкой обновления.</summary>
    [RelayCommand]
    public async Task LoadCharactersAsync()
    {
        if (!IsLoggedIn)
        {
            SetStatus("CharImport_NotAuthenticated", ImportStatusKind.Warning);
            return;
        }

        IsBusy = true;
        SetStatus("CharImport_LoadingList", ImportStatusKind.Info);
        try
        {
            var result = await _api.GetCharactersAsync();
            if (!result.Ok)
            {
                ReportApiError(result.Error, result.Message, result.RetryAfterSeconds);
                return;
            }

            _allCharacters = await ResolveClassNamesAsync(result.Value!);
            if (_allCharacters.Count == 0)
            {
                Characters.Clear();
                SetStatus("CharImport_NoCharacters", ImportStatusKind.Warning);
                return;
            }

            RebuildLeagues();
            ApplyFilter();
            // «Персонаж найден» — только когда нашёлся ИМЕННО тот, к кому привязан
            // билд: иначе курсор просто стоит на первом в списке.
            var foundBound = IsReimport && await SelectBoundCharacterAsync();
            SetStatus(foundBound ? "CharImport_UpdateReady" : "CharImport_ListLoaded",
                ImportStatusKind.Info);
        }
        finally { IsBusy = false; }
    }

    /// <summary>API отдаёт внутренний id восхождения ("Witch2") — переводим его в
    /// человекочитаемое имя средствами дерева, как это делает Lua-ImportTab.</summary>
    private async Task<IReadOnlyList<CharacterSummary>> ResolveClassNamesAsync(
        IReadOnlyList<CharacterSummary> characters)
    {
        try
        {
            var host = await _hostTask;
            return await Task.Run(() => (IReadOnlyList<CharacterSummary>)characters
                .Select(c => c with { Class = host.ResolveCharacterClassName(c.Class) })
                .ToList());
        }
        catch
        {
            // Движок ещё не готов — показываем как есть, имена классов не критичны.
            return characters;
        }
    }

    private void RebuildLeagues()
    {
        var leagues = _allCharacters.Select(c => c.League).Distinct().OrderBy(l => l).ToList();
        var previous = SelectedLeague;
        Leagues.Clear();
        Leagues.Add(AllLeagues);
        foreach (var l in leagues) Leagues.Add(l);
        // Молча: сеттер сам перефильтрует, если значение поменялось.
        SelectedLeague = leagues.Contains(previous) ? previous : AllLeagues;
    }

    /// <summary>Ставит курсор на персонажа, к которому привязан билд: по имени, а у
    /// билдов из оригинального PoB (там лежит только sha1 имени) — по хешу. Если
    /// персонаж отфильтрован лигой или поиском — сбрасывает фильтры, иначе кнопка
    /// «Обновить» молча обновила бы билд из чужого персонажа.</summary>
    /// <returns>true — привязанный персонаж есть в аккаунте и выбран.</returns>
    private async Task<bool> SelectBoundCharacterAsync()
    {
        if (!_binding.HasValue) return false;

        var name = _binding.CharacterName;
        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(_binding.Hash))
            name = await ResolveNameByHashAsync(_binding.Hash);
        if (string.IsNullOrEmpty(name)) return false;

        // Персонаж есть в аккаунте, но скрыт фильтром — показываем всё, чтобы он нашёлся.
        if (!Characters.Any(c => Match(c, name)) &&
            _allCharacters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedLeague = AllLeagues;
            Search = "";
        }

        var found = Characters.FirstOrDefault(c => Match(c, name));
        if (found is not null) SelectedCharacter = found;
        return found is not null;

        static bool Match(CharacterEntryViewModel c, string name) =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Имя персонажа по sha1 из билда — перебором персонажей аккаунта,
    /// ровно как это делает Lua-ImportTab (ImportTab.lua:578).</summary>
    private async Task<string?> ResolveNameByHashAsync(string hash)
    {
        try
        {
            var host = await _hostTask;
            return await Task.Run(() => _allCharacters
                .FirstOrDefault(c => string.Equals(host.Sha1(c.Name), hash, StringComparison.OrdinalIgnoreCase))
                ?.Name);
        }
        catch
        {
            return null;
        }
    }

    private void ApplyFilter()
    {
        var previous = SelectedCharacter?.Name;
        var search = Search.Trim();
        Characters.Clear();
        foreach (var c in _allCharacters
                     .Where(c => SelectedLeague == AllLeagues || c.League == SelectedLeague)
                     .Where(c => search.Length == 0 ||
                                 c.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                 c.Class.Contains(search, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            Characters.Add(new CharacterEntryViewModel(c));
        }
        SelectedCharacter = Characters.FirstOrDefault(c => c.Name == previous) ?? Characters.FirstOrDefault();
    }

    // ── Импорт ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportAsync()
    {
        if (!CanImport || SelectedCharacter is null) return;

        IsBusy = true;
        SetStatus("CharImport_Downloading", ImportStatusKind.Info);
        try
        {
            var json = await _api.GetCharacterJsonAsync(SelectedCharacter.Name);
            if (!json.Ok)
            {
                ReportApiError(json.Error, json.Message, json.RetryAfterSeconds);
                return;
            }

            SetStatus("CharImport_Importing", ImportStatusKind.Info);
            var host = await _hostTask;
            var options = new CharacterImportOptions
            {
                PassiveTree = ImportPassiveTree,
                ItemsAndSkills = ImportItemsAndSkills,
                // Флаги удаления остаются на значениях по умолчанию (true): при импорте
                // в новый билд чистить нечего, а при обновлении открытого билда старое
                // дерево/предметы/умения должны уступить место пришедшим из игры.
                IgnoreWeaponSwap = IgnoreWeaponSwap,
            };

            if (IsReimport)
                await ReimportIntoOpenBuildAsync(host, json.Value!, options);
            else
                await ImportAsNewBuildAsync(host, json.Value!, options);
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(LocalizationService.Get("Msg_Error"), ex.Message);
            StatusKind = ImportStatusKind.Error;
        }
        finally { IsBusy = false; }
    }

    private async Task ImportAsNewBuildAsync(LuaHost host, string json, CharacterImportOptions options)
    {
        var result = await Task.Run(() => { host.NewBuild(); return host.ImportCharacter(json, options); });
        if (!ReportImport(result)) return;

        host.SetCharacterBinding(SelectedCharacter!.Name, _oauth.AccountName);
        var xml = await Task.Run(host.SaveBuildToXml);
        if (xml is null)
        {
            SetStatus("Msg_ErrorSave", ImportStatusKind.Error);
            return;
        }

        var folder = _buildsFolder;
        Directory.CreateDirectory(folder);
        var name = UniqueName(folder, SanitizeFileName(SelectedCharacter!.Name));
        var filePath = Path.Combine(folder, name + ".xml");
        await File.WriteAllTextAsync(filePath, xml);

        SetStatus("CharImport_Done", ImportStatusKind.Ok);
        _onImported?.Invoke(filePath);
        CloseRequested?.Invoke();
    }

    /// <summary>«Обновить из игры»: персонаж ложится поверх открытого билда, файл билда
    /// перезаписывается, страница билда обновляет вкладки.</summary>
    private async Task ReimportIntoOpenBuildAsync(LuaHost host, string json, CharacterImportOptions options)
    {
        var name = SelectedCharacter!.Name;
        var result = await Task.Run(() =>
        {
            var r = host.ImportCharacter(json, options);
            if (r.Ok) host.SetCharacterBinding(name, _oauth.AccountName);
            return r;
        });
        if (!ReportImport(result)) return;

        var xml = await Task.Run(() => _build!.SaveBuildToXml());
        if (xml is null)
        {
            SetStatus("Msg_ErrorSave", ImportStatusKind.Error);
            return;
        }
        await File.WriteAllTextAsync(_xmlPath!, xml);

        _binding = new CharacterBinding(name, _oauth.AccountName, _binding.Hash);
        OnPropertyChanged(nameof(BoundCharacterName));
        OnPropertyChanged(nameof(HasBinding));
        OnPropertyChanged(nameof(BoundCharacterLine));

        if (_afterReimport is not null) await _afterReimport();

        SetStatus("CharImport_UpdateDone", ImportStatusKind.Ok);
        CloseRequested?.Invoke();
    }

    private bool ReportImport(CharacterImportResult result)
    {
        if (result.Ok) return true;
        StatusMessage = string.Format(LocalizationService.Get("CharImport_ImportFailed"), result.Error);
        StatusKind = ImportStatusKind.Error;
        return false;
    }

    private void ReportApiError(CharacterApiError error, string? message, int retryAfter)
    {
        switch (error)
        {
            case CharacterApiError.NotAuthenticated:
                // Токен отозван или протух без refresh: чистим его, иначе IsLoggedIn
                // так и остался бы true и окно показывало бы пустой список вместо входа.
                Logout();
                break;
            case CharacterApiError.PrivateProfile:
                SetStatus("CharImport_PrivateProfile", ImportStatusKind.Error);
                break;
            case CharacterApiError.NotFound:
                SetStatus("CharImport_NotFound", ImportStatusKind.Error);
                break;
            case CharacterApiError.RateLimited:
                StatusMessage = string.Format(
                    LocalizationService.Get("CharImport_RateLimited"), retryAfter);
                StatusKind = ImportStatusKind.Error;
                break;
            default:
                StatusMessage = string.Format(
                    LocalizationService.Get("CharImport_NetworkError"), message ?? "");
                StatusKind = ImportStatusKind.Error;
                break;
        }
    }

    private void SetStatus(string key, ImportStatusKind kind)
    {
        StatusMessage = LocalizationService.Get(key);
        StatusKind = kind;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim() is { Length: > 0 } trimmed ? trimmed : "Imported Character";
    }

    private static string UniqueName(string dir, string baseName)
    {
        if (!File.Exists(Path.Combine(dir, baseName + ".xml"))) return baseName;
        for (int i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!File.Exists(Path.Combine(dir, candidate + ".xml"))) return candidate;
        }
    }
}
