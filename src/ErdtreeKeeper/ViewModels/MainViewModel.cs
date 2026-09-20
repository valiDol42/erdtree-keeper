using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ErdtreeKeeper.Core;
using ErdtreeKeeper.Updates;

namespace ErdtreeKeeper.ViewModels;

/// <summary>Насколько свежий сейв на диске.</summary>
public enum Freshness { Unknown, Fresh, Stale, Old }

/// <summary>По какому столбцу отсортирован список снимков.</summary>
public enum SnapshotSort { Name, Created }

/// <summary>Аккаунт в списке: длинный SteamID неудобен, поэтому его можно подписать.</summary>
public sealed class AccountItem(SaveAccount account, string? alias)
{
    public SaveAccount Account { get; } = account;
    public string? Alias { get; set; } = alias;

    public string Display
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(Alias)
                ? Account.SteamId
                : $"{Alias}  ·  {Account.SteamId}";

            // Папка из чужого профиля Windows подписывается его именем: иначе
            // две одинаковые строки в списке ничем не отличаются.
            return Account.WindowsProfile is { Length: > 0 } profile
                ? $"{name}  ·  {Loc.Get("source.fromProfile", profile)}"
                : name;
        }
    }
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly PortableSettings _settings;
    private readonly SnapshotService _snapshotService;
    private readonly DispatcherTimer _clock;

    // Автоснимок делается не по расписанию, а когда игра действительно
    // записала сейв: ждём, пока файл перестанет меняться.
    private DateTime? _pendingWrite;
    private DateTime _lastAutoSnapshot = DateTime.MinValue;
    private long _lastSeenLength;
    private DateTime _lastSeenWrite;

    // Отпечаток файла, которому соответствует прочитанный контекст: время
    // записи и длина. По нему видно, что сейв с тех пор переписан.
    private DateTime _contextWrittenAt;
    private long _contextLength;

    // Тик и кнопка могут захотеть прочитать сейв в одну и ту же секунду.
    // Второе чтение ничего не добавит - только лишние 29 МБ с диска.
    private bool _readingContext;

    public MainViewModel()
    {
        _settings = PortableSettings.Load();

        // Язык применяется здесь и только здесь: сохранённый выбор, а без него
        // язык системы. Само чтение настроек ничего не переключает, иначе
        // повторный вызов менял бы язык уже открытому окну.
        Loc.Current.Language = _settings.Language;
        _strings = Loc.Snapshot();

        Log = new ActivityLog();
        _snapshotService = new SnapshotService(Log);
        Updates = new UpdateService(Log);

        // Команды создаются до присваивания свойств: их сеттеры дёргают
        // RaiseCanExecuteChanged, и в обратном порядке это падало бы.
        RefreshCommand = new AsyncRelayCommand(RefreshEverythingAsync);
        CreateSnapshotCommand = new AsyncRelayCommand(CreateSnapshotAsync, () => CanCreateSnapshot);
        RestoreCommand = new AsyncRelayCommand(RestoreAsync, () => SelectedSnapshot is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteSnapshotAsync, () => SelectedRows.Count > 0);
        RenameCommand = new AsyncRelayCommand(RenameSnapshotAsync, () => SelectedSnapshot is not null);
        CheckIntegrityCommand = new AsyncRelayCommand(CheckIntegrityAsync, () => SelectedSaveFile is not null);
        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => SelectedSaveFile is not null);
        PickFolderCommand = new AsyncRelayCommand(PickSnapshotFolderAsync);
        PickAutoFolderCommand = new AsyncRelayCommand(PickAutoFolderAsync);
        SortByNameCommand = new RelayCommand(() => SortBy(SnapshotSort.Name));
        SortByDateCommand = new RelayCommand(() => SortBy(SnapshotSort.Created));
        RenameAccountCommand = new AsyncRelayCommand(RenameAccountAsync, () => SelectedAccount is not null);
        SelectAllCommand = new RelayCommand(() => SetAllSelected(true), () => Snapshots.Count > 0);
        ClearSelectionCommand = new RelayCommand(() => SetAllSelected(false), () => SelectedRows.Count > 0);
        OpenSnapshotFolderCommand = new RelayCommand(() => OpenInExplorer(ListFolder));
        OpenGameFolderCommand = new RelayCommand(() => OpenInExplorer(SelectedAccount?.Account.Path));
        ExportLogCommand = new AsyncRelayCommand(ExportLogAsync);
        DismissOnboardingCommand = new RelayCommand(DismissOnboarding);

        AddLocationCommand = new AsyncRelayCommand(() => AppendFromContextAsync(useBoss: false), () => CanUseMapNames);
        AddBossCommand = new AsyncRelayCommand(() => AppendFromContextAsync(useBoss: true), () => CanUseMapNames);
        AddBeforeCommand = new RelayCommand(() => SnapshotName = SnapshotNaming.WithPairSuffix(SnapshotName, SnapshotNaming.BeforeSuffix));
        AddAfterCommand = new RelayCommand(() => SnapshotName = SnapshotNaming.WithPairSuffix(SnapshotName, SnapshotNaming.AfterSuffix));
        AddTimeCommand = new RelayCommand(() => SnapshotName = SnapshotNaming.AppendTime(SnapshotName, DateTime.Now));
        ClearNameCommand = new RelayCommand(() => SnapshotName = "");

        AddGameCommand = new AsyncRelayCommand(AddGameAsync);
        RemoveGameCommand = new AsyncRelayCommand(RemoveGameAsync, () => SelectedGame is { BuiltIn: false });
        CheckUpdatesCommand = new AsyncRelayCommand(OpenUpdatesAsync);

        foreach (var game in _settings.AllGames()) Games.Add(game);
        _game = GameProfiles.ById(Games, _settings.Values.SelectedGameId);
        _selectedGame = _game;

        LoadGameState();
        AutoSnapshotEnabled = _settings.Values.AutoSnapshotEnabled;
        ShowOnboarding = !_settings.Values.OnboardingDone;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _clock.Tick += (_, _) => OnTick();
        _clock.Start();

        Log.Info(Loc.Get("log.settingsPath", _settings.Path));
        if (!_settings.IsPortable)
        {
            Log.Warn(Loc.Get("log.settingsInAppData"));
        }
    }

    // ─── Состояние ──────────────────────────────────────────────────────

    private Dictionary<string, string> _strings;

    /// <summary>
    /// Строки для разметки: {Binding L[ключ]}.
    ///
    /// Здесь лежит снимок таблицы, а не сама таблица. Привязку к индексатору
    /// Avalonia по уведомлению "Item[]" не обновляет - при смене языка окно
    /// оставалось наполовину переведённым. Подмена всего словаря - обычная
    /// смена свойства, и на неё привязки реагируют без исключений.
    /// </summary>
    public IReadOnlyDictionary<string, string> L => _strings;

    /// <summary>
    /// Язык интерфейса. Две половины переключателя ведут себя как радиокнопки:
    /// щелчок по уже выбранной ничего не выключает.
    /// </summary>
    public bool IsEnglish
    {
        get => Loc.Current.IsEnglish;
        set
        {
            if (!value) { OnPropertyChanged(); return; }
            SetLanguage(Lang.En);
        }
    }

    public bool IsRussian
    {
        get => !Loc.Current.IsEnglish;
        set
        {
            if (!value) { OnPropertyChanged(); return; }
            SetLanguage(Lang.Ru);
        }
    }

    private void SetLanguage(Lang language)
    {
        if (Loc.Current.Language == language) return;

        Loc.Current.Language = language;
        _settings.Values.Language = language.ToString();
        _settings.Save();

        ApplyLanguage();
    }

    /// <summary>
    /// Перерисовывает окно после смены языка.
    ///
    /// Пустое имя свойства - соглашение INotifyPropertyChanged: обновится всё,
    /// что привязано к модели. Раньше здесь стоял список свойств вручную, и он
    /// подвёл ровно так, как такие списки и подводят: новое свойство в него
    /// дописать забыли.
    /// </summary>
    private void ApplyLanguage()
    {
        // Переименование пунктов списка источников сбрасывает выбор в поле, а
        // оно двусторонее и успевает записать в модель -1. Поэтому выбор
        // запоминается до перерисовки и возвращается после неё.
        var source = SnapshotSourceIndex;

        _strings = Loc.Snapshot();
        RefreshSourceNames();
        UpdateFreshness();
        SayAgain();

        OnPropertyChanged(string.Empty);

        SnapshotSourceIndex = source;
        OnPropertyChanged(nameof(SnapshotSourceIndex));
    }

    public ActivityLog Log { get; }

    // ─── Игра ───────────────────────────────────────────────────────────

    private GameProfile _game;
    private GameProfile? _selectedGame;

    /// <summary>Игры, между которыми можно переключаться: встроенные и свои.</summary>
    public ObservableCollection<GameProfile> Games { get; } = [];

    /// <summary>С сохранениями какой игры сейчас работает окно.</summary>
    public GameProfile Game => _game;

    public GameProfile? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!Set(ref _selectedGame, value) || value is null) return;
            SwitchTo(value);
        }
    }

    /// <summary>
    /// Переключение на другую игру.
    ///
    /// Всё, что относилось к прежней, сбрасывается: прочитанный сейв, слежение
    /// за записью, список снимков. Иначе в имени снимка Dark Souls оказалось бы
    /// место из Elden Ring, а список предлагал бы восстановить чужой файл.
    /// </summary>
    private void SwitchTo(GameProfile game)
    {
        if (_game.Id == game.Id) return;

        _game = game;
        _settings.Values.SelectedGameId = game.Id;
        _settings.Save();

        SaveContext = null;
        ResetWriteTracking();
        LoadGameState();

        Log.Info(Loc.Get("log.gameSwitched", game.Name), game.ResolveRoot());

        RefreshAccounts();
        RefreshSnapshots();
        UpdateFreshness();
        NotifyGameChanged();
    }

    /// <summary>Папки и имя снимка, запомненные для этой игры.</summary>
    private void LoadGameState()
    {
        var state = _settings.StateOf(_game);

        // Прямо в поля, минуя свойства: сеттер папки снимков тянет за собой
        // папку автосохранений, и при переключении игры он перенёс бы туда
        // путь от предыдущей.
        _snapshotFolder = state.SnapshotFolder ?? _settings.DefaultSnapshotFolder(_game);
        _autoFolder = state.AutoSnapshotFolder ?? Path.Combine(_snapshotFolder, SnapshotService.AutoFolder);
        _snapshotName = state.LastSnapshotName ?? "";

        OnPropertyChanged(nameof(SnapshotFolder));
        OnPropertyChanged(nameof(AutoFolder));
        OnPropertyChanged(nameof(SnapshotName));
        OnPropertyChanged(nameof(ListFolder));
        OnPropertyChanged(nameof(SnapshotPreview));
    }

    private void NotifyGameChanged()
    {
        OnPropertyChanged(nameof(Game));
        OnPropertyChanged(nameof(CanUseMapNames));
        OnPropertyChanged(nameof(CanRemoveGame));
        OnPropertyChanged(nameof(GameHint));
        OnPropertyChanged(nameof(IntegrityNote));
        OnPropertyChanged(nameof(CharacterLine));
        OnPropertyChanged(nameof(PlaceLine));
        OnPropertyChanged(nameof(HasContext));
        OnPropertyChanged(nameof(SnapshotPreview));
        OnPropertyChanged(nameof(SteamCloudWarning));

        AddLocationCommand.RaiseCanExecuteChanged();
        AddBossCommand.RaiseCanExecuteChanged();
        RemoveGameCommand.RaiseCanExecuteChanged();
        UpdateCreateAvailability();
    }

    /// <summary>Имя снимка можно собрать из места только там, где программа знает карту.</summary>
    public bool CanUseMapNames => _game.HasMapKnowledge;

    /// <summary>
    /// Убрать можно только добавленную игру. Для встроенной кнопка не
    /// показывается вовсе: выключенная кнопка вызывает вопрос "почему", а
    /// отсутствующая - нет.
    /// </summary>
    public bool CanRemoveGame => SelectedGame is { BuiltIn: false };

    /// <summary>Короткая подсказка о том, чего ждать от этой игры.</summary>
    public string GameHint => _game.HasMapKnowledge
        ? Loc.Get("game.hintFull")
        : Loc.Get("game.hintBasic", _game.Name);

    /// <summary>
    /// Что именно проверяет кнопка целостности - у разных игр по-разному, и
    /// обещать одинаково нельзя.
    /// </summary>
    public string IntegrityNote => _game.Layout switch
    {
        SaveLayout.EldenRing => Loc.Get("game.checkFull"),
        SaveLayout.Bnd4 => Loc.Get("game.checkContainer"),
        _ => Loc.Get("game.checkCopyOnly"),
    };

    public ObservableCollection<AccountItem> Accounts { get; } = [];
    public ObservableCollection<SaveFile> SaveFiles { get; } = [];
    public ObservableCollection<SnapshotRow> Snapshots { get; } = [];

    private AccountItem? _selectedAccount;
    public AccountItem? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (!Set(ref _selectedAccount, value)) return;
            _settings.StateOf(_game).LastAccountId = value?.Account.SteamId;
            _settings.Save();
            RefreshSaveFiles();
            OnPropertyChanged(nameof(SteamCloudWarning));
            RenameAccountCommand.RaiseCanExecuteChanged();
            OpenGameFolderCommand.RaiseCanExecuteChanged();
        }
    }

    private SaveFile? _selectedSaveFile;
    public SaveFile? SelectedSaveFile
    {
        get => _selectedSaveFile;
        set
        {
            if (!Set(ref _selectedSaveFile, value)) return;
            _settings.StateOf(_game).LastFileName = value?.Name;
            _settings.Save();

            // Подсказка относится к конкретному файлу: при смене её надо гасить,
            // иначе рядом с новым сейвом висит локация от прежнего.
            SaveContext = null;
            ResetWriteTracking();
            UpdateFreshness();
            CheckIntegrityCommand.RaiseCanExecuteChanged();
            AnalyzeCommand.RaiseCanExecuteChanged();
            UpdateCreateAvailability();
        }
    }

    /// <summary>
    /// Что выбрано. Признак живёт в самой строке, поэтому переживает
    /// обновление списка и не зависит от внутренней модели выбора Avalonia.
    /// </summary>
    public List<SnapshotRow> SelectedRows => [.. Snapshots.Where(r => r.IsSelected)];

    /// <summary>Единственный выбранный - для операций над одним файлом.</summary>
    public SnapshotRow? SelectedSnapshot
    {
        get
        {
            var selected = SelectedRows;
            return selected.Count == 1 ? selected[0] : null;
        }
    }

    /// <summary>Строка сообщила, что её выбрали или сняли выбор.</summary>
    private void OnRowSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedRows));
        OnPropertyChanged(nameof(SelectedSnapshot));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(DeleteLabel));

        RestoreCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
        RenameCommand.RaiseCanExecuteChanged();
        ClearSelectionCommand.RaiseCanExecuteChanged();
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var row in Snapshots) row.IsSelected = selected;
    }

    /// <summary>Сколько файлов затронет следующая операция.</summary>
    public string SelectionSummary
    {
        get
        {
            var count = SelectedRows.Count;
            return count switch
            {
                0 => Snapshots.Count == 0 ? "" : Loc.Get("list.nothingSelected"),
                1 => Loc.Get("list.oneSelected", Snapshots.Count),
                _ => Loc.Get("list.manySelected", count, Loc.Plural(count, "plural.file"), Snapshots.Count),
            };
        }
    }

    /// <summary>На кнопке удаления видно, сколько файлов уйдёт.</summary>
    public string DeleteLabel => SelectedRows.Count > 1 ? Loc.Get("list.deleteCount", SelectedRows.Count) : Loc.Get("list.delete");

    private string _snapshotFolder = "";
    public string SnapshotFolder
    {
        get => _snapshotFolder;
        set
        {
            var previous = _snapshotFolder;
            if (!Set(ref _snapshotFolder, value)) return;
            _settings.StateOf(_game).SnapshotFolder = value;
            _settings.Save();

            // Если папка автосохранений так и осталась подпапкой прежней - она
            // переезжает следом. Выбранную вручную не трогаем.
            if (previous.Length > 0 && _autoFolder == Path.Combine(previous, SnapshotService.AutoFolder))
            {
                AutoFolder = Path.Combine(value, SnapshotService.AutoFolder);
            }

            OnPropertyChanged(nameof(ListFolder));
            RefreshSnapshots();
            UpdateCreateAvailability();
        }
    }

    private string _autoFolder = "";
    /// <summary>
    /// Куда складывать автосохранения. По умолчанию - подпапка рядом со
    /// снимками, но её можно увести куда угодно: например, на другой диск,
    /// чтобы копии пережили переустановку системы.
    /// </summary>
    public string AutoFolder
    {
        get => _autoFolder;
        set
        {
            if (!Set(ref _autoFolder, value)) return;
            _settings.StateOf(_game).AutoSnapshotFolder = value;
            _settings.Save();
            OnPropertyChanged(nameof(ListFolder));
            if (IsAutoFolder) RefreshSnapshots();
        }
    }

    /// <summary>Не чаще одного автосохранения за столько минут.</summary>
    public int AutoMinutes
    {
        get => _settings.Values.AutoSnapshotMinutes;
        set
        {
            var clamped = Math.Clamp(value, 1, 120);
            if (_settings.Values.AutoSnapshotMinutes == clamped) return;
            _settings.Values.AutoSnapshotMinutes = clamped;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Сколько последних автосохранений хранить.</summary>
    public int AutoKeep
    {
        get => _settings.Values.AutoSnapshotKeep;
        set
        {
            var clamped = Math.Clamp(value, 1, 200);
            if (_settings.Values.AutoSnapshotKeep == clamped) return;
            _settings.Values.AutoSnapshotKeep = clamped;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    // NumericUpDown отдаёт decimal?, а хранить настройку удобнее целым числом.
    // Эти две обёртки существуют только ради привязки.
    public decimal? AutoMinutesValue
    {
        get => AutoMinutes;
        set { if (value is { } v) AutoMinutes = (int)v; }
    }

    public decimal? AutoKeepValue
    {
        get => AutoKeep;
        set { if (value is { } v) AutoKeep = (int)v; }
    }

    /// <summary>
    /// Что показывает список: отобранные вручную снимки или автосохранения.
    ///
    /// Коллекция одна на всё время работы, и при смене языка в ней меняются
    /// строки, а не она сама. Подмена списка целиком сбрасывала выбор, и
    /// поле оставалось пустым.
    /// </summary>
    public ObservableCollection<string> SnapshotSources { get; } =
        [Loc.Get("list.manual"), Loc.Get("list.auto")];

    private void RefreshSourceNames()
    {
        SnapshotSources[0] = Loc.Get("list.manual");
        SnapshotSources[1] = Loc.Get("list.auto");
    }

    private int _snapshotSourceIndex;
    public int SnapshotSourceIndex
    {
        get => _snapshotSourceIndex;
        set
        {
            if (!Set(ref _snapshotSourceIndex, value)) return;
            OnPropertyChanged(nameof(ListFolder));
            OnPropertyChanged(nameof(IsAutoFolder));
            OnPropertyChanged(nameof(EmptyStateHint));
            OnPropertyChanged(nameof(NameSortLabel));
            OnPropertyChanged(nameof(DateSortLabel));
            RefreshSnapshots();
        }
    }

    public bool IsAutoFolder => SnapshotSourceIndex == 1;

    /// <summary>
    /// Папка, с которой работает список.
    ///
    /// Все действия под списком - переименовать, удалить, восстановить -
    /// работают с тем, что видно, а не с какой-то другой папкой.
    /// </summary>
    public string ListFolder => IsAutoFolder ? AutoFolder : SnapshotFolder;

    // ─── Сортировка списка ──────────────────────────────────────────────

    private SnapshotSort _sortField = SnapshotSort.Created;
    private bool _sortDescending = true;

    /// <summary>
    /// Переключает сортировку. Повторный щелчок по тому же столбцу меняет
    /// направление - как в проводнике, чтобы не пришлось объяснять.
    /// </summary>
    public void SortBy(SnapshotSort field)
    {
        if (_sortField == field)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortField = field;
            // Даты по умолчанию сверху свежие, имена - от А: так ожидаемее.
            _sortDescending = field == SnapshotSort.Created;
        }

        OnPropertyChanged(nameof(NameSortLabel));
        OnPropertyChanged(nameof(DateSortLabel));
        RefreshSnapshots();
    }

    public string NameSortLabel => SortLabel(Loc.Get("list.name"), SnapshotSort.Name);
    public string DateSortLabel => SortLabel(Loc.Get("list.modified"), SnapshotSort.Created);

    private string SortLabel(string title, SnapshotSort field) =>
        _sortField == field ? $"{title}  {(_sortDescending ? "↓" : "↑")}" : title;

    private IEnumerable<Snapshot> ApplySort(IEnumerable<Snapshot> snapshots) => (_sortField, _sortDescending) switch
    {
        (SnapshotSort.Name, false) => snapshots.OrderBy(s => s.Name, NaturalFileNameComparer.Instance),
        (SnapshotSort.Name, true) => snapshots.OrderByDescending(s => s.Name, NaturalFileNameComparer.Instance),
        (_, false) => snapshots.OrderBy(s => s.Created),
        _ => snapshots.OrderByDescending(s => s.Created),
    };

    public string EmptyStateHint => IsAutoFolder
        ? Loc.Get("list.emptyAuto")
        : Loc.Get("list.emptyManual");

    private string _snapshotName = "";
    public string SnapshotName
    {
        get => _snapshotName;
        set
        {
            if (!Set(ref _snapshotName, value)) return;
            _settings.StateOf(_game).LastSnapshotName = value;
            _settings.Save();
            OnPropertyChanged(nameof(SnapshotPreview));
            UpdateCreateAvailability();
        }
    }

    private SaveContext? _saveContext;
    public SaveContext? SaveContext
    {
        get => _saveContext;
        private set
        {
            if (!Set(ref _saveContext, value)) return;
            OnPropertyChanged(nameof(CharacterLine));
            OnPropertyChanged(nameof(PlaceLine));
            OnPropertyChanged(nameof(HasContext));
        }
    }

    private string _status = Loc.Get("status.ready");
    public string Status { get => _status; private set => Set(ref _status, value); }

    private IBrush _statusBrush = Brush("TextSecondaryBrush");
    public IBrush StatusBrush { get => _statusBrush; private set => Set(ref _statusBrush, value); }

    private bool _busy;
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value)) UpdateCreateAvailability();
        }
    }

    private bool _showOnboarding;
    public bool ShowOnboarding { get => _showOnboarding; private set => Set(ref _showOnboarding, value); }

    private bool _autoSnapshotEnabled;
    public bool AutoSnapshotEnabled
    {
        get => _autoSnapshotEnabled;
        set
        {
            if (!Set(ref _autoSnapshotEnabled, value)) return;
            _settings.Values.AutoSnapshotEnabled = value;
            _settings.Save();
            ResetWriteTracking();
            Log.Info(value
                ? Loc.Get("log.autoOn")
                : Loc.Get("log.autoOff"));
        }
    }

    // ─── Свежесть сейва ─────────────────────────────────────────────────

    private Freshness _freshness = Freshness.Unknown;
    public Freshness FreshnessLevel
    {
        get => _freshness;
        private set
        {
            if (!Set(ref _freshness, value)) return;
            OnPropertyChanged(nameof(FreshnessBrush));
            OnPropertyChanged(nameof(FreshnessWash));
        }
    }

    private string _freshnessText = Loc.Get("fresh.none");
    public string FreshnessText { get => _freshnessText; private set => Set(ref _freshnessText, value); }

    public IBrush FreshnessBrush => FreshnessLevel switch
    {
        Freshness.Fresh => Brush("FreshBrush"),
        Freshness.Stale => Brush("WarnBrush"),
        Freshness.Old => Brush("DangerBrush"),
        _ => Brush("TextMutedBrush"),
    };

    public IBrush FreshnessWash => FreshnessLevel switch
    {
        Freshness.Fresh => Brush("FreshWashBrush"),
        Freshness.Stale => Brush("WarnWashBrush"),
        Freshness.Old => Brush("DangerWashBrush"),
        _ => Brush("SurfaceRaisedBrush"),
    };

    // ─── Производные строки ─────────────────────────────────────────────

    public bool HasContext => SaveContext is not null;

    public string CharacterLine => SaveContext is null
        ? (_game.HasMapKnowledge ? Loc.Get("source.readHint") : Loc.Get("source.noReader", _game.Name))
        : $"{SaveContext.Character.Name}  ·  {Loc.Get("card.levelShort", SaveContext.Character.Level)}  ·  {SaveContext.Character.ClassName}";

    public string PlaceLine => SaveContext?.Summary ?? "";

    public string SnapshotPreview
    {
        get
        {
            var file = SnapshotNaming.ToFileName(SnapshotName, Extension);
            if (file.Length == 0) return Loc.Get("name.enter");

            var full = Path.Combine(SnapshotFolder, file);
            return File.Exists(full) ? Loc.Get("name.willOverwrite", file) : Loc.Get("name.willCreate", file);
        }
    }

    /// <summary>Подпись внизу окна: что это и какая сборка.</summary>
    public string FooterText => $"{AppInfo.Name} {AppInfo.FullVersion}";

    /// <summary>
    /// Чем полезен трекер - числами из того же справочника, которым программа
    /// называет снимки. Не рекламное обещание, а то, что лежит внутри файла.
    /// </summary>
    public string TrackerPitch
    {
        get
        {
            var graces = MapPoints.Graces.Count;
            var bosses = MapPoints.Bosses.Count;
            return Loc.Get("tracker.pitch",
                graces, Loc.Plural(graces, "plural.place"),
                bosses, Loc.Plural(bosses, "plural.boss"));
        }
    }

    public string SettingsPath => _settings.Path;
    public bool IsPortable => _settings.IsPortable;

    /// <summary>Состояние файла настроек на диске - читается в момент запроса.</summary>
    public string SettingsFileState => _settings.DescribeFile();

    /// <summary>Steam синхронизирует папку сохранений с облаком - об этом надо предупредить.</summary>
    public string? SteamCloudWarning => SelectedAccount?.Account.HasSteamCloudMarker == true
        ? Loc.Get("warn.steamCloud")
        : null;

    /// <summary>
    /// Расширение снимка повторяет расширение исходного файла: у Seamless
    /// Co-op это .co2, у Dark Souls - свои, а у добавленной игры - какое есть.
    /// </summary>
    private string Extension => SelectedSaveFile is { } file
        ? _game.ExtensionOf(file.Name)
        : _game.DefaultExtension;

    private bool CanCreateSnapshot =>
        !IsBusy && SelectedSaveFile is not null && SnapshotNaming.ToFileName(SnapshotName, Extension).Length > 0;

    // ─── Команды ────────────────────────────────────────────────────────

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CreateSnapshotCommand { get; }
    public AsyncRelayCommand RestoreCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public AsyncRelayCommand RenameCommand { get; }
    public AsyncRelayCommand CheckIntegrityCommand { get; }
    public AsyncRelayCommand AnalyzeCommand { get; }
    public AsyncRelayCommand PickFolderCommand { get; }
    public AsyncRelayCommand PickAutoFolderCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand SortByNameCommand { get; }
    public RelayCommand SortByDateCommand { get; }
    public AsyncRelayCommand RenameAccountCommand { get; }
    public AsyncRelayCommand ExportLogCommand { get; }
    public RelayCommand OpenSnapshotFolderCommand { get; }
    public RelayCommand OpenGameFolderCommand { get; }
    public RelayCommand DismissOnboardingCommand { get; }
    public AsyncRelayCommand AddLocationCommand { get; }
    public AsyncRelayCommand AddBossCommand { get; }
    public RelayCommand AddBeforeCommand { get; }
    public RelayCommand AddAfterCommand { get; }
    public RelayCommand AddTimeCommand { get; }
    public RelayCommand ClearNameCommand { get; }
    public AsyncRelayCommand AddGameCommand { get; }
    public AsyncRelayCommand RemoveGameCommand { get; }
    public AsyncRelayCommand CheckUpdatesCommand { get; }

    // ─── Диалоги задаёт окно ────────────────────────────────────────────

    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, string?, Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, string, string, Task<string?>>? PromptAsync { get; set; }
    public Func<string, string, Task<string?>>? SaveFileAsync { get; set; }
    public Func<string, string, Task>? ShowReportAsync { get; set; }

    /// <summary>Окно добавления игры. Возвращает описание игры или ничего.</summary>
    public Func<Task<CustomGame?>>? AskForGameAsync { get; set; }

    /// <summary>Окно обновлений - единственное место, откуда программа выходит в сеть.</summary>
    public Func<Task>? ShowUpdatesAsync { get; set; }

    // ─── Действия ───────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        RefreshAccounts();
        RefreshSnapshots();

        if (SelectedSaveFile is not null && _game.HasMapKnowledge) await AnalyzeAsync();

        await CheckUpdatesOnStartAsync();
    }

    private async Task RefreshEverythingAsync()
    {
        RefreshAccounts();
        RefreshSnapshots();
        SayKey("status.listRefreshed", "TextSecondaryBrush");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Где искать сохранения. В работе это всегда папка выбранной игры;
    /// подменить её может только инструмент снимков экрана - чтобы показывать
    /// вымышленный аккаунт вместо настоящего.
    /// </summary>
    public string? SavesRootOverride { get; set; }

    public string SavesRoot
    {
        get => SavesRootOverride ?? _game.ResolveRoot();
        set => SavesRootOverride = value;
    }

    public void RefreshAccounts()
    {
        var wanted = SelectedAccount?.Account.SteamId ?? _settings.StateOf(_game).LastAccountId;

        var root = SavesRoot;

        Accounts.Clear();
        foreach (var account in GameSaves.FindAccounts(_game, root))
        {
            _settings.Values.Aliases.TryGetValue(account.SteamId, out var alias);
            Accounts.Add(new AccountItem(account, alias));
        }

        if (Accounts.Count == 0)
        {
            // Пустая папка - обычное дело: игра установлена не у всех. Это не
            // ошибка программы, поэтому в сообщении стоит имя игры и путь, по
            // которому она искала.
            SelectedAccount = null;
            SaveFiles.Clear();
            SelectedSaveFile = null;
            // В сообщении - все места, где программа искала. У одной игры их
            // бывает несколько, и "не найдено" без списка не даёт понять, куда
            // смотреть: у Dark Souls Remastered сейв лежит в "Документах",
            // а не там, где у остальных игр FromSoftware.
            var searched = SavesRootOverride is null
                ? string.Join("  ·  ", _game.ResolveOwnRoots())
                : root;

            // Про чужие профили - одной строкой: их пути человеку ничего не
            // говорят, а знать, что там тоже смотрели, полезно.
            var others = SavesRootOverride is null ? WindowsProfiles.Others().Count : 0;
            if (others > 0) searched += "  ·  " + Loc.Get("source.alsoProfiles", others);

            SayKey("status.noSaveFolderFor", "WarnBrush", _game.Name, searched);
            Log.Warn(Loc.Get("log.noSavesFor", _game.Name), searched);
            return;
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Account.SteamId == wanted) ?? Accounts[0];
        Log.Read(Loc.Get("log.accountsFound", Accounts.Count), root);
    }

    private void RefreshSaveFiles()
    {
        var wanted = SelectedSaveFile?.Name ?? _settings.StateOf(_game).LastFileName;

        SaveFiles.Clear();
        if (SelectedAccount is null) return;

        foreach (var file in GameSaves.FindSaveFiles(_game, SelectedAccount.Account.Path))
        {
            SaveFiles.Add(file);
        }

        SelectedSaveFile =
            SaveFiles.FirstOrDefault(f => f.Name == wanted)
            ?? SaveFiles.FirstOrDefault(f => f.Name.Equals(_game.PrimaryFile, StringComparison.OrdinalIgnoreCase))
            ?? SaveFiles.FirstOrDefault();
    }

    public void RefreshSnapshots()
    {
        // Строки пересоздаются, поэтому выбор возвращаем по именам: иначе он
        // молча пропадал бы после каждого обновления списка.
        var wanted = SelectedRows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Snapshots) row.PropertyChanged -= OnRowPropertyChanged;
        Snapshots.Clear();

        foreach (var snapshot in ApplySort(_snapshotService.List(ListFolder, _game)))
        {
            var row = new SnapshotRow(snapshot) { IsSelected = wanted.Contains(snapshot.Name) };
            row.PropertyChanged += OnRowPropertyChanged;
            Snapshots.Add(row);
        }

        OnRowSelectionChanged();
        OnPropertyChanged(nameof(SnapshotPreview));
        SelectAllCommand.RaiseCanExecuteChanged();
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SnapshotRow.IsSelected)) OnRowSelectionChanged();
    }

    /// <summary>Читает сейв по кнопке - с сообщением о том, что происходит.</summary>
    private async Task AnalyzeAsync() => await ReadContextAsync(announce: true);

    /// <summary>
    /// Устарел ли прочитанный контекст.
    ///
    /// Сравнение на неравенство, а не на "файл новее". Восстановление снимка
    /// кладёт в игру старый файл, и его время записи оказывается РАНЬШЕ того,
    /// когда мы читали текущий: проверка "новее" такую подмену пропускала, и
    /// в имени снимка оставалось прежнее место. Длина здесь же - на случай,
    /// если время совпало с точностью до тика.
    /// </summary>
    private bool ContextIsStale
    {
        get
        {
            if (SelectedSaveFile is null) return false;
            if (SaveContext is null) return true;

            try
            {
                var info = new FileInfo(SelectedSaveFile.Path);
                if (!info.Exists) return false;
                return info.LastWriteTime != _contextWrittenAt || info.Length != _contextLength;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Читает сейв и показывает, кто где стоит.
    ///
    /// announce = false - фоновое обновление после того, как игра дописала
    /// сейв. Данные должны стать свежими, но занимать собой строку состояния и
    /// мигать индикатором занятости на ровном месте незачем.
    /// </summary>
    private async Task ReadContextAsync(bool announce)
    {
        if (SelectedSaveFile is null || _readingContext) return;

        // Разбирать содержимое умеем только у Elden Ring. Для остальных игр
        // карточка персонажа остаётся пустой, и это честнее, чем показывать
        // выдуманные подробности.
        if (!_game.HasMapKnowledge)
        {
            if (announce) SayKey("status.noReader", "WarnBrush", _game.Name);
            return;
        }

        _readingContext = true;

        DateTime writtenAt;
        long length;
        try
        {
            var info = new FileInfo(SelectedSaveFile.Path);
            if (!info.Exists) return;
            writtenAt = info.LastWriteTime;
            length = info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _readingContext = false;
            return;
        }

        if (announce)
        {
            IsBusy = true;
            SayKey("status.reading", "TextSecondaryBrush");
        }

        try
        {
            var path = SelectedSaveFile.Path;
            Log.Read(Loc.Get("log.parsingSave"), path);

            var context = await Task.Run(async () =>
            {
                var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                return SaveContextReader.Read(bytes);
            });

            SaveContext = context;
            // Отпечаток снимаем ДО чтения: если игра успела переписать сейв,
            // пока мы его разбирали, контекст уже устарел, и следующая проверка
            // это увидит, а не сочтёт его свежим.
            _contextWrittenAt = writtenAt;
            _contextLength = length;

            if (announce)
            {
                SayKey(context is null ? "status.noCharacters" : "status.saveRead",
                    context is null ? "WarnBrush" : "FreshBrush");
            }
            else if (context is not null)
            {
                SayKey("status.saveRefreshed", "FreshBrush");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(Loc.Get("err.readSave", ex.Message), SelectedSaveFile.Path);
            if (announce) SayKey("err.readSave", "DangerBrush", ex.Message);
        }
        catch (Exception ex) when (!announce)
        {
            // Фоновое чтение никто не ждёт: исключение отсюда ушло бы в никуда.
            // Разборщик кривого файла может бросить что угодно - пусть это
            // останется записью в журнале, а не тихой потерей.
            Log.Error(Loc.Get("err.readSave", ex.GetType().Name + ": " + ex.Message), SelectedSaveFile.Path);
        }
        finally
        {
            _readingContext = false;
            if (announce) IsBusy = false;
        }
    }

    /// <summary>Пересчитывает все контрольные суммы и показывает отчёт.</summary>
    private async Task CheckIntegrityAsync()
    {
        if (SelectedSaveFile is null) return;

        IsBusy = true;
        SayKey("status.checking", "TextSecondaryBrush");
        try
        {
            var path = SelectedSaveFile.Path;
            var game = _game;
            var check = await Task.Run(async () =>
            {
                var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                return SaveIntegrity.Inspect(game, bytes);
            });

            var text = SaveIntegrity.BuildReport(game, check, SelectedSaveFile.Name);
            Log.Read(check.Ok ? Loc.Get("log.integrityOk") : Loc.Get("log.integrityBad", check.Problem ?? ""), path);

            if (!check.Ok) Say(check.Problem ?? Loc.Get("status.damaged"), "DangerBrush");
            else if (game.Layout == SaveLayout.EldenRing) SayKey("status.integrityOk", "FreshBrush");
            else SayKey("status.integrityBasic", "FreshBrush");

            if (ShowReportAsync is not null) await ShowReportAsync(Loc.Get("dlg.integrityTitle"), text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SayKey("err.check", "DangerBrush", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateSnapshotAsync()
    {
        if (SelectedSaveFile is null) return;

        var fileName = SnapshotNaming.ToFileName(SnapshotName, Extension);
        if (fileName.Length == 0) return;

        var destination = Path.Combine(SnapshotFolder, fileName);
        var overwrite = false;

        if (File.Exists(destination))
        {
            if (ConfirmAsync is null) return;
            overwrite = await ConfirmAsync(
                Loc.Get("dlg.overwriteTitle"),
                Loc.Get("dlg.overwriteBody", fileName),
                Loc.Get("dlg.overwrite"));
            if (!overwrite) { SayKey("status.cancelled", "TextSecondaryBrush"); return; }
        }

        IsBusy = true;
        SayKey("status.snapshotting", "TextSecondaryBrush");
        try
        {
            var result = await _snapshotService.CreateAsync(
                SelectedSaveFile.Path, SnapshotFolder, fileName, overwrite, game: _game);

            Say(result.Message, result.Success ? "FreshBrush" : "DangerBrush");
            if (result.Success) RefreshSnapshots();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync()
    {
        if (SelectedSnapshot is null || SelectedAccount is null || ConfirmAsync is null) return;

        var targetName = SelectedSaveFile?.Name ?? _game.PrimaryFile;
        if (targetName is null) { SayKey("status.noTargetFile", "WarnBrush"); return; }

        var target = Path.Combine(SelectedAccount.Account.Path, targetName);

        var warning = GameSaves.IsGameRunning(_game)
            ? "\n\n" + Loc.Get("warn.gameRunning")
            : "";

        var cloud = SelectedAccount.Account.HasSteamCloudMarker
            ? "\n\n" + Loc.Get("warn.cloudRestore")
            : "";

        var confirmed = await ConfirmAsync(
            Loc.Get("dlg.restoreTitle"),
            Loc.Get("dlg.restoreBody", SelectedSnapshot.Name, target)
            + warning + cloud,
            Loc.Get("list.restoreVerb"));
        if (!confirmed) { SayKey("status.cancelled", "TextSecondaryBrush"); return; }

        IsBusy = true;
        SayKey("status.restoring", "TextSecondaryBrush");
        try
        {
            var result = await _snapshotService.RestoreAsync(
                SelectedSnapshot.Path, target, game: _game);
            Say(result.Message, result.Success ? "FreshBrush" : "DangerBrush");
            if (result.Success)
            {
                ResetWriteTracking();
                UpdateFreshness();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Удаляет все выбранные файлы.
    ///
    /// В подтверждении перечисляются имена, а не только количество: удаление
    /// необратимо, и "удалить 7 файлов" не даёт шанса заметить, что в выборку
    /// попало лишнее.
    /// </summary>
    private async Task DeleteSnapshotAsync()
    {
        if (SelectedRows.Count == 0 || ConfirmAsync is null) return;

        var doomed = SelectedRows;

        const int shown = 12;
        var names = string.Join(NewLine, doomed.Take(shown).Select(s => "  " + s.Name));
        if (doomed.Count > shown) names += NewLine + "  " + Loc.Get("dlg.andMore", doomed.Count - shown);

        var title = doomed.Count == 1
            ? Loc.Get("dlg.deleteOneTitle")
            : Loc.Get("dlg.deleteManyTitle", doomed.Count, Loc.Plural(doomed.Count, "plural.snapshot"));

        var confirmed = await ConfirmAsync(
            title,
            names + NewLine + NewLine + Loc.Get("dlg.deleteBody"),
            doomed.Count == 1 ? Loc.Get("list.delete") : Loc.Get("dlg.deleteMany", doomed.Count));
        if (!confirmed) { SayKey("status.cancelled", "TextSecondaryBrush"); return; }

        var removed = 0;
        var failed = new List<string>();

        foreach (var snapshot in doomed)
        {
            if (_snapshotService.Delete(snapshot.Path).Success) removed++;
            else failed.Add(snapshot.Name);
        }

        RefreshSnapshots();

        if (failed.Count == 0)
        {
            if (removed == 1) SayKey("status.deletedOne", "TextSecondaryBrush");
            else SayKey("status.deletedMany", "TextSecondaryBrush", removed);
        }
        else
        {
            SayKey("status.deletedPartly", "DangerBrush", removed, failed.Count, failed[0]);
        }

        await Task.CompletedTask;
    }

    private async Task RenameSnapshotAsync()
    {
        if (SelectedSnapshot is null || PromptAsync is null) return;

        var current = SelectedSnapshot.Name;
        var answer = await PromptAsync(Loc.Get("dlg.renameTitle"), Loc.Get("dlg.renameBody"), current);
        if (string.IsNullOrWhiteSpace(answer) || answer == current) return;

        var newName = SnapshotNaming.ToFileName(answer, Path.GetExtension(current));
        var result = _snapshotService.Rename(SelectedSnapshot.Path, newName);
        Say(result.Message, result.Success ? "FreshBrush" : "DangerBrush");
        if (result.Success) RefreshSnapshots();
    }

    private async Task RenameAccountAsync()
    {
        if (SelectedAccount is null || PromptAsync is null) return;

        var answer = await PromptAsync(
            Loc.Get("dlg.aliasTitle"),
            Loc.Get("dlg.aliasBody", SelectedAccount.Account.SteamId),
            SelectedAccount.Alias ?? "");
        if (answer is null) return;

        var id = SelectedAccount.Account.SteamId;
        if (string.IsNullOrWhiteSpace(answer)) _settings.Values.Aliases.Remove(id);
        else _settings.Values.Aliases[id] = answer.Trim();
        _settings.Save();

        RefreshAccounts();
    }

    /// <summary>
    /// Добавляет игру, которой нет во встроенном списке.
    ///
    /// Всё, что для этого нужно, - папка с сохранениями. Работа с ними дальше
    /// идёт ровно та же, что и у Elden Ring: копия, проверка совпадения,
    /// обязательная резервная копия перед возвратом в игру.
    /// </summary>
    private async Task AddGameAsync()
    {
        if (AskForGameAsync is null) return;

        var added = await AskForGameAsync();
        if (added is null) return;

        if (string.IsNullOrWhiteSpace(added.Id)) added.Id = GameProfiles.NewCustomId();

        _settings.Values.CustomGames.Add(added);
        _settings.Save();

        var profile = GameProfiles.FromCustom(added);
        Games.Add(profile);
        Log.Info(Loc.Get("log.gameAdded", profile.Name), profile.ResolveRoot());

        SelectedGame = profile;
    }

    /// <summary>
    /// Убирает игру из списка. Снимки при этом остаются на диске: удалять
    /// чужие файлы за компанию программа не станет.
    /// </summary>
    private async Task RemoveGameAsync()
    {
        if (SelectedGame is not { BuiltIn: false } game || ConfirmAsync is null) return;

        var confirmed = await ConfirmAsync(
            Loc.Get("game.removeTitle"),
            Loc.Get("game.removeBody", game.Name, _settings.StateOf(game).SnapshotFolder
                                                   ?? _settings.DefaultSnapshotFolder(game)),
            Loc.Get("game.remove"));
        if (!confirmed) { SayKey("status.cancelled", "TextSecondaryBrush"); return; }

        _settings.Values.CustomGames.RemoveAll(g => g.Id == game.Id);
        _settings.Values.Games.Remove(game.Id);
        _settings.Save();

        Games.Remove(game);
        Log.Info(Loc.Get("log.gameRemoved", game.Name));

        SelectedGame = Games.FirstOrDefault(g => g.Id == GameProfiles.EldenRingId) ?? Games.FirstOrDefault();
    }

    // ─── Обновления ─────────────────────────────────────────────────────

    /// <summary>Работа с сетью. Ничего не делает, пока её об этом не попросят.</summary>
    public UpdateService Updates { get; }

    /// <summary>
    /// Разрешение обращаться к GitHub. Пусто означает, что не спрашивали - и
    /// до ответа ни один запрос не уходит.
    /// </summary>
    public bool? UpdatesAllowed
    {
        get => _settings.Values.UpdatesAllowed;
        set
        {
            if (_settings.Values.UpdatesAllowed == value) return;
            _settings.Values.UpdatesAllowed = value;
            _settings.Save();
            Log.Info(value == true ? Loc.Get("log.updAllowed") : Loc.Get("log.updDenied"));
            OnPropertyChanged();
        }
    }

    /// <summary>Проверять при запуске. Отдельно от разрешения: одно дело - можно, другое - каждый раз.</summary>
    public bool UpdatesCheckOnStart
    {
        get => _settings.Values.UpdatesCheckOnStart;
        set
        {
            if (_settings.Values.UpdatesCheckOnStart == value) return;
            _settings.Values.UpdatesCheckOnStart = value;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>Версия, о которой попросили не напоминать.</summary>
    public string? SkippedVersion
    {
        get => _settings.Values.SkippedVersion;
        set
        {
            _settings.Values.SkippedVersion = value;
            _settings.Save();
        }
    }

    /// <summary>Отметка о том, что проверка состоялась - её видно в окне обновлений.</summary>
    public void RememberUpdateCheck()
    {
        _settings.Values.LastUpdateCheck = DateTime.UtcNow.ToString("o");
        _settings.Save();
        OnPropertyChanged(nameof(LastUpdateCheckText));
    }

    public string LastUpdateCheckText =>
        DateTimeOffset.TryParse(_settings.Values.LastUpdateCheck, out var when)
            ? Loc.Get("upd.lastCheck", when.ToLocalTime().ToString("dd.MM.yyyy HH:mm"))
            : Loc.Get("upd.neverChecked");

    private string? _updateNotice;

    /// <summary>Полоска в шапке: вышла новая версия. Появляется только после проверки.</summary>
    public string? UpdateNotice
    {
        get => _updateNotice;
        private set
        {
            if (!Set(ref _updateNotice, value)) return;
            OnPropertyChanged(nameof(HasUpdateNotice));
        }
    }

    public bool HasUpdateNotice => !string.IsNullOrEmpty(_updateNotice);

    public void NoticeVersion(string? version) =>
        UpdateNotice = version is null ? null : Loc.Get("upd.notice", version);

    private async Task OpenUpdatesAsync()
    {
        if (ShowUpdatesAsync is null) return;
        await ShowUpdatesAsync();
    }

    /// <summary>
    /// Тихая проверка при запуске - только если её разрешили и попросили.
    ///
    /// Молчит обо всём, кроме появления новой версии: сообщение "у вас и так
    /// последняя" при каждом запуске никому не нужно.
    /// </summary>
    private async Task CheckUpdatesOnStartAsync()
    {
        if (_settings.Values.UpdatesAllowed != true || !_settings.Values.UpdatesCheckOnStart) return;

        var status = await Updates.CheckAsync();
        RememberUpdateCheck();

        if (status.State != UpdateState.Available || status.Release is null) return;
        if (_settings.Values.SkippedVersion == status.Release.Version) return;

        NoticeVersion(status.Release.Version);
    }

    private async Task PickSnapshotFolderAsync()
    {
        if (PickFolderAsync is null) return;

        var picked = await PickFolderAsync(Loc.Get("dlg.pickSnapshotFolder"), SnapshotFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (Reject(picked)) return;

        SnapshotFolder = picked;
        Log.Info(Loc.Get("log.snapFolderChanged"), picked);
    }

    private async Task PickAutoFolderAsync()
    {
        if (PickFolderAsync is null) return;

        var picked = await PickFolderAsync(Loc.Get("dlg.pickAutoFolder"), AutoFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        if (Reject(picked)) return;

        AutoFolder = picked;
        Log.Info(Loc.Get("log.autoFolderChanged"), picked);
    }

    /// <summary>
    /// Не даёт складывать снимки в папку игры.
    ///
    /// Оттуда ротация удаляла бы файлы игры, а снимок с именем ER0000
    /// перезаписал бы живой сейв без резервной копии - в обход всего, что
    /// обещано в разделе "Что программа делает".
    /// </summary>
    private bool Reject(string folder)
    {
        if (!GameSaves.IsInsideGameFolder(_game, folder, SavesRootOverride)) return false;

        Log.Warn(Loc.Get("log.gameFolderRejected"), folder);
        SayKey("status.gameFolderRejected", "DangerBrush");
        return true;
    }

    private async Task ExportLogAsync()
    {
        if (SaveFileAsync is null) return;

        var path = await SaveFileAsync(Loc.Get("dlg.saveLog"), $"erdtree-keeper-log-{DateTime.Now:yyyy-MM-dd}.txt");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await Log.ExportAsync(path);
            SayKey("status.logSaved", "FreshBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SayKey("err.logSave", "DangerBrush", ex.Message);
        }
    }

    /// <summary>
    /// Подставляет место в имя снимка, предварительно убедившись, что оно
    /// свежее.
    ///
    /// Тик может не успеть: между записью сейва игрой и нажатием кнопки
    /// проходит меньше времени, чем нужно, чтобы признать запись законченной.
    /// Поэтому здесь перечитывание не по расписанию, а по факту устаревания.
    /// </summary>
    private async Task AppendFromContextAsync(bool useBoss)
    {
        if (ContextIsStale) await ReadContextAsync(announce: false);
        AppendFromContext(useBoss);
    }

    private void AppendFromContext(bool useBoss)
    {
        if (SaveContext is null)
        {
            SayKey("status.readFirst", "WarnBrush");
            return;
        }

        var point = useBoss ? SaveContext.Boss : SaveContext.Location;
        if (point is null)
        {
            SayKey(useBoss ? "status.noBossNearby" : "status.noLocation", "WarnBrush");
            return;
        }

        var name = SnapshotName;
        if (SaveContext.IsDlc) name = SnapshotNaming.EnsureDlcTag(name);
        SnapshotName = SnapshotNaming.Append(name, point.Display);
    }

    private void DismissOnboarding()
    {
        ShowOnboarding = false;
        _settings.Values.OnboardingDone = true;
        _settings.Save();
    }

    // ─── Часы: свежесть и автоснимок ────────────────────────────────────

    private void OnTick()
    {
        UpdateFreshness();
        TrackSaveWrites();
    }

    /// <summary>
    /// Один шаг слежения за файлом - ровно то, что делает таймер окна.
    ///
    /// Вынесено в публичный метод ради проверяемости: в headless-стенде таймер
    /// не тикает, и без этой точки сценарий "игра записала сейв - место
    /// обновилось само" проверить нечем.
    /// </summary>
    public void PollSaveFile() => TrackSaveWrites();

    private void UpdateFreshness()
    {
        if (SelectedSaveFile is null || !File.Exists(SelectedSaveFile.Path))
        {
            FreshnessLevel = Freshness.Unknown;
            FreshnessText = Loc.Get("fresh.none");
            return;
        }

        DateTime written;
        try { written = File.GetLastWriteTime(SelectedSaveFile.Path); }
        catch (IOException) { return; }

        var age = DateTime.Now - written;

        FreshnessText = Loc.Get("fresh.written", written.ToString("dd.MM.yyyy HH:mm:ss"), Humanize(age));

        // Игра сбрасывает сейв на диск не мгновенно. Пока запись не случилась,
        // копия будет содержать состояние ДО последних событий.
        if (age.TotalMinutes < 2)
        {
            FreshnessLevel = Freshness.Fresh;
        }
        else if (age.TotalMinutes < 15)
        {
            FreshnessLevel = Freshness.Stale;
        }
        else
        {
            FreshnessLevel = Freshness.Old;
        }
    }

    /// <summary>
    /// Следит за тем, как игра пишет сейв: сначала ждём изменения файла, потом
    /// - пока он перестанет меняться.
    ///
    /// Раньше это работало только при включённом автосохранении и служило
    /// только ему. Но по тому же событию надо перечитывать и содержимое сейва:
    /// иначе "+ локация" подставляет место, где персонаж был на момент запуска
    /// программы, и игроку приходится каждый раз вручную жать "Прочитать сейв"
    /// - причём догадаться об этом неоткуда.
    /// </summary>
    private void TrackSaveWrites()
    {
        if (SelectedSaveFile is null) return;

        FileInfo info;
        try { info = new FileInfo(SelectedSaveFile.Path); if (!info.Exists) return; }
        catch (IOException) { return; }

        var changed = info.LastWriteTime != _lastSeenWrite || info.Length != _lastSeenLength;
        _lastSeenWrite = info.LastWriteTime;
        _lastSeenLength = info.Length;

        if (changed)
        {
            _pendingWrite = DateTime.Now;
            return;
        }

        if (_pendingWrite is null) return;

        // Файл не менялся достаточно долго - запись закончилась.
        if ((DateTime.Now - _pendingWrite.Value).TotalSeconds < 6) return;
        _pendingWrite = null;

        // Сейв переписан - перечитываем его, чтобы карточка персонажа и кнопки
        // имени показывали, где он стоит сейчас. Это делается независимо от
        // автосохранения: место в имени снимка нужно и тем, кто снимает вручную.
        if (ContextIsStale) _ = ReadContextAsync(announce: false);

        if (!AutoSnapshotEnabled) return;

        // Нижняя граница частоты: игра пишет сейв часто, и без неё папка
        // забивалась бы почти одинаковыми копиями.
        if ((DateTime.Now - _lastAutoSnapshot).TotalMinutes < AutoMinutes) return;
        _lastAutoSnapshot = DateTime.Now;

        _ = TakeAutoSnapshotAsync();
    }

    private async Task TakeAutoSnapshotAsync()
    {
        if (SelectedSaveFile is null) return;

        var folder = AutoFolder;
        var path = SelectedSaveFile.Path;

        try
        {
            // Место в имени берётся из сейва, но читать его умеет только
            // разборщик Elden Ring. Для остальных игр имя складывается из
            // названия игры и времени - большего программа о них не знает.
            var context = _game.HasMapKnowledge
                ? await Task.Run(async () =>
                {
                    var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                    return SaveContextReader.Read(bytes);
                })
                : null;

            var name = _game.HasMapKnowledge
                ? SnapshotNaming.AutoName(context, DateTime.Now, Extension)
                : SnapshotNaming.AutoName(null, DateTime.Now, Extension);

            var result = await _snapshotService.CreateAsync(
                path, folder, name, overwrite: false, game: _game);

            if (result.Success)
            {
                var removed = _snapshotService.Rotate(folder, AutoKeep, _game);
                if (removed > 0) SayKey("status.autoSnapRotated", "FreshBrush", name, removed);
                else SayKey("status.autoSnap", "FreshBrush", name);
                RefreshSnapshots();
            }
        }
        catch (Exception ex)
        {
            // Задача запущена без ожидания: всё, что вылетит отсюда, иначе
            // пропадёт без следа. В журнал - и тип, чтобы было по чему искать.
            Log.Error(Loc.Get("err.autoSnap", ex is IOException or UnauthorizedAccessException
                ? ex.Message
                : ex.GetType().Name + ": " + ex.Message), path);
        }
    }

    private void ResetWriteTracking()
    {
        _pendingWrite = null;
        _lastSeenWrite = default;
        _lastSeenLength = 0;

        // Контекст относился к прежнему файлу - отпечаток тоже обнуляем, иначе
        // новый сейв сочтётся уже прочитанным.
        _contextWrittenAt = default;
        _contextLength = 0;
    }

    // ─── Мелочи ─────────────────────────────────────────────────────────

    /// <summary>Перенос строки для текстов диалогов.</summary>
    private static string NewLine => Environment.NewLine;

    private void UpdateCreateAvailability() => CreateSnapshotCommand.RaiseCanExecuteChanged();

    // Последняя строка состояния - не журнал, а описание текущего положения
    // дел, поэтому при смене языка она должна смениться вместе с окном. Для
    // этого хранится ключ, а не только готовый текст.
    private string? _statusKey;
    private object[] _statusArgs = [];
    private string _statusBrushKey = "TextSecondaryBrush";

    private void SayKey(string key, string brushKey, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        _statusBrushKey = brushKey;

        Status = args.Length == 0 ? Loc.Get(key) : Loc.Get(key, args);
        StatusBrush = Brush(brushKey);
    }

    /// <summary>
    /// Состояние из готовой строки - например, ответ службы копирования.
    /// Такую строку пересчитать нельзя, поэтому ключ сбрасывается.
    /// </summary>
    private void Say(string message, string brushKey)
    {
        _statusKey = null;
        _statusArgs = [];
        _statusBrushKey = brushKey;

        Status = message;
        StatusBrush = Brush(brushKey);
    }

    private void SayAgain()
    {
        if (_statusKey is { } key) SayKey(key, _statusBrushKey, _statusArgs);
    }

    private static IBrush Brush(string key)
    {
        var app = Application.Current;
        if (app is not null && app.TryGetResource(key, app.ActualThemeVariant, out var value) &&
            value is IBrush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    private static string Humanize(TimeSpan span)
    {
        if (span.TotalSeconds < 60) return Loc.Get("fresh.secondsAgo", span.TotalSeconds.ToString("N0"));
        if (span.TotalMinutes < 60) return Loc.Get("fresh.minutesAgo", span.TotalMinutes.ToString("N0"));
        if (span.TotalHours < 24) return Loc.Get("fresh.hoursAgo", span.TotalHours.ToString("N0"));
        return Loc.Get("fresh.daysAgo", span.TotalDays.ToString("N0"));
    }

    private static void OpenInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.ComponentModel.Win32Exception)
        {
            // Не открылось - не повод падать.
        }
    }

    public void Shutdown()
    {
        _clock.Stop();
        _settings.Save();
    }
}
