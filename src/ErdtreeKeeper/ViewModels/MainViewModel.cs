using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ErdtreeKeeper.Core;

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

    public string Display => string.IsNullOrWhiteSpace(Alias)
        ? Account.SteamId
        : $"{Alias}  ·  {Account.SteamId}";
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

    public MainViewModel()
    {
        _settings = PortableSettings.Load();
        Log = new ActivityLog();
        _snapshotService = new SnapshotService(Log);

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

        AddLocationCommand = new RelayCommand(() => AppendFromContext(useBoss: false));
        AddBossCommand = new RelayCommand(() => AppendFromContext(useBoss: true));
        AddBeforeCommand = new RelayCommand(() => SnapshotName = SnapshotNaming.WithPairSuffix(SnapshotName, SnapshotNaming.BeforeSuffix));
        AddAfterCommand = new RelayCommand(() => SnapshotName = SnapshotNaming.WithPairSuffix(SnapshotName, SnapshotNaming.AfterSuffix));
        ClearNameCommand = new RelayCommand(() => SnapshotName = "");

        SnapshotFolder = _settings.Values.SnapshotFolder ?? Path.Combine(PortableSettings.AppFolder, "Снимки");
        AutoFolder = _settings.Values.AutoSnapshotFolder
                     ?? Path.Combine(SnapshotFolder, SnapshotService.AutoFolder);
        SnapshotName = _settings.Values.LastSnapshotName ?? "";
        AutoSnapshotEnabled = _settings.Values.AutoSnapshotEnabled;
        ShowOnboarding = !_settings.Values.OnboardingDone;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _clock.Tick += (_, _) => OnTick();
        _clock.Start();

        Log.Info($"Настройки: {_settings.Path}");
        if (!_settings.IsPortable)
        {
            Log.Warn("Папка программы недоступна для записи, настройки лежат в AppData");
        }
    }

    // ─── Состояние ──────────────────────────────────────────────────────

    public ActivityLog Log { get; }

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
            _settings.Values.LastAccountId = value?.Account.SteamId;
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
            _settings.Values.LastFileName = value?.Name;
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
                0 => Snapshots.Count == 0 ? "" : "ничего не выбрано",
                1 => $"выбран 1 файл из {Snapshots.Count}",
                _ => $"выбрано {count} {Plural(count, "файл", "файла", "файлов")} из {Snapshots.Count}",
            };
        }
    }

    /// <summary>На кнопке удаления видно, сколько файлов уйдёт.</summary>
    public string DeleteLabel => SelectedRows.Count > 1 ? $"Удалить ({SelectedRows.Count})" : "Удалить";

    private string _snapshotFolder = "";
    public string SnapshotFolder
    {
        get => _snapshotFolder;
        set
        {
            var previous = _snapshotFolder;
            if (!Set(ref _snapshotFolder, value)) return;
            _settings.Values.SnapshotFolder = value;
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
            _settings.Values.AutoSnapshotFolder = value;
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

    /// <summary>Что показывает список: отобранные вручную снимки или автосохранения.</summary>
    public string[] SnapshotSources { get; } = ["Снимки", "Автосохранения"];

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

    public string NameSortLabel => SortLabel("Имя", SnapshotSort.Name);
    public string DateSortLabel => SortLabel("Изменён", SnapshotSort.Created);

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
        ? "Включите автосохранение слева. Снимок появится здесь после того, как игра запишет сейв."
        : "Сядьте у благодати, чтобы игра записала сейв, задайте имя слева и нажмите «Сделать снимок».";

    private string _snapshotName = "";
    public string SnapshotName
    {
        get => _snapshotName;
        set
        {
            if (!Set(ref _snapshotName, value)) return;
            _settings.Values.LastSnapshotName = value;
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

    private string _status = "Готово";
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
                ? "Автоснимки включены: снимок делается после того, как игра запишет сейв"
                : "Автоснимки выключены");
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

    private string _freshnessText = "Сейв не выбран";
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
        ? "Нажмите \"Прочитать сейв\", чтобы увидеть персонажа"
        : $"{SaveContext.Character.Name}  ·  {SaveContext.Character.Level} ур.  ·  {SaveContext.Character.ClassName}";

    public string PlaceLine => SaveContext?.Summary ?? "";

    public string SnapshotPreview
    {
        get
        {
            var file = SnapshotNaming.ToFileName(SnapshotName, Extension);
            if (file.Length == 0) return "Введите имя снимка";

            var full = Path.Combine(SnapshotFolder, file);
            return File.Exists(full) ? $"Будет перезаписан: {file}" : $"Будет создан: {file}";
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
            return $"{graces} {Plural(graces, "место", "места", "мест")} благодати "
                   + $"и {bosses} {Plural(bosses, "босс", "босса", "боссов")} на карте. "
                   + "Прогресс подтягивается из вашего сохранения.";
        }
    }

    /// <summary>
    /// Русское склонение по числу. Числа берутся из справочника и меняются
    /// вместе с ним, поэтому форму слова нельзя вписать руками.
    /// </summary>
    private static string Plural(int count, string one, string few, string many)
    {
        // 11-14 - исключение: "одиннадцать мест", а не "одиннадцать место".
        if (count % 100 is >= 11 and <= 14) return many;

        return (count % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    public string SettingsPath => _settings.Path;
    public bool IsPortable => _settings.IsPortable;

    /// <summary>Состояние файла настроек на диске - читается в момент запроса.</summary>
    public string SettingsFileState => _settings.DescribeFile();

    /// <summary>Steam синхронизирует папку сохранений с облаком - об этом надо предупредить.</summary>
    public string? SteamCloudWarning => SelectedAccount?.Account.HasSteamCloudMarker == true
        ? "У этого аккаунта включена синхронизация Steam Cloud. Восстанавливайте сейв только при полностью закрытой игре, иначе Steam может вернуть облачную версию."
        : null;

    private string Extension =>
        SelectedSaveFile?.IsSeamlessCoop == true ? ".co2" : ".sl2";

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
    public RelayCommand AddLocationCommand { get; }
    public RelayCommand AddBossCommand { get; }
    public RelayCommand AddBeforeCommand { get; }
    public RelayCommand AddAfterCommand { get; }
    public RelayCommand ClearNameCommand { get; }

    // ─── Диалоги задаёт окно ────────────────────────────────────────────

    public Func<string, string, string, Task<bool>>? ConfirmAsync { get; set; }
    public Func<string, string?, Task<string?>>? PickFolderAsync { get; set; }
    public Func<string, string, string, Task<string?>>? PromptAsync { get; set; }
    public Func<string, string, Task<string?>>? SaveFileAsync { get; set; }
    public Func<string, string, Task>? ShowReportAsync { get; set; }

    // ─── Действия ───────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        RefreshAccounts();
        RefreshSnapshots();

        if (SelectedSaveFile is not null) await AnalyzeAsync();
    }

    private async Task RefreshEverythingAsync()
    {
        RefreshAccounts();
        RefreshSnapshots();
        Say("Список обновлён", "TextSecondaryBrush");
        await Task.CompletedTask;
    }

    public void RefreshAccounts()
    {
        var wanted = SelectedAccount?.Account.SteamId ?? _settings.Values.LastAccountId;

        Accounts.Clear();
        foreach (var account in GameSaves.FindAccounts())
        {
            _settings.Values.Aliases.TryGetValue(account.SteamId, out var alias);
            Accounts.Add(new AccountItem(account, alias));
        }

        if (Accounts.Count == 0)
        {
            Say($"Папка сохранений не найдена: {GameSaves.DefaultRoot}", "DangerBrush");
            Log.Warn("Сохранения Elden Ring не найдены", GameSaves.DefaultRoot);
            return;
        }

        SelectedAccount = Accounts.FirstOrDefault(a => a.Account.SteamId == wanted) ?? Accounts[0];
        Log.Read($"Найдено аккаунтов: {Accounts.Count}", GameSaves.DefaultRoot);
    }

    private void RefreshSaveFiles()
    {
        var wanted = SelectedSaveFile?.Name ?? _settings.Values.LastFileName;

        SaveFiles.Clear();
        if (SelectedAccount is null) return;

        foreach (var file in GameSaves.FindSaveFiles(SelectedAccount.Account.Path))
        {
            SaveFiles.Add(file);
        }

        SelectedSaveFile =
            SaveFiles.FirstOrDefault(f => f.Name == wanted)
            ?? SaveFiles.FirstOrDefault(f => f.Name == "ER0000.sl2")
            ?? SaveFiles.FirstOrDefault();
    }

    public void RefreshSnapshots()
    {
        // Строки пересоздаются, поэтому выбор возвращаем по именам: иначе он
        // молча пропадал бы после каждого обновления списка.
        var wanted = SelectedRows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Snapshots) row.PropertyChanged -= OnRowPropertyChanged;
        Snapshots.Clear();

        foreach (var snapshot in ApplySort(_snapshotService.List(ListFolder)))
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

    /// <summary>Читает сейв и показывает, кто где стоит.</summary>
    private async Task AnalyzeAsync()
    {
        if (SelectedSaveFile is null) return;

        IsBusy = true;
        Say("Читаю сейв...", "TextSecondaryBrush");
        try
        {
            var path = SelectedSaveFile.Path;
            Log.Read("Разбираю сохранение", path);

            var context = await Task.Run(async () =>
            {
                var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                return SaveContextReader.Read(bytes);
            });

            SaveContext = context;
            Say(context is null ? "Персонажи в сейве не найдены" : "Сейв прочитан",
                context is null ? "WarnBrush" : "FreshBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Не удалось прочитать сейв: {ex.Message}", SelectedSaveFile.Path);
            Say($"Не удалось прочитать сейв: {ex.Message}", "DangerBrush");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Пересчитывает все контрольные суммы и показывает отчёт.</summary>
    private async Task CheckIntegrityAsync()
    {
        if (SelectedSaveFile is null) return;

        IsBusy = true;
        Say("Проверяю целостность...", "TextSecondaryBrush");
        try
        {
            var path = SelectedSaveFile.Path;
            var report = await Task.Run(async () =>
            {
                var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                return Sl2File.CheckIntegrity(bytes);
            });

            var text = BuildIntegrityReport(report, SelectedSaveFile.Name);
            Log.Read(report.AllOk ? "Целостность в порядке" : $"Повреждённых блоков: {report.BadCount}", path);
            Say(report.AllOk ? "Сейв целый: все контрольные суммы сошлись" : $"Повреждённых блоков: {report.BadCount}",
                report.AllOk ? "FreshBrush" : "DangerBrush");

            if (ShowReportAsync is not null) await ShowReportAsync("Проверка целостности", text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"Не удалось проверить: {ex.Message}", "DangerBrush");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildIntegrityReport(Sl2File.IntegrityReport report, string fileName)
    {
        var lines = new List<string> { $"Файл: {fileName}", "" };

        if (!report.FileRecognised)
        {
            lines.Add("Это не похоже на сохранение Elden Ring: нет подписи BND4 в начале файла.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add(report.SizeAsExpected
            ? $"Размер: {report.ActualSize:N0} байт - как у обычного сейва."
            : $"Размер: {report.ActualSize:N0} байт - отличается от обычных {Sl2File.VanillaSize:N0}.");
        lines.Add("");
        lines.Add("Игра проверяет каждый блок по контрольной сумме MD5 и отказывается");
        lines.Add("загружать блок, если сумма не сошлась. Ниже - результат по каждому.");
        lines.Add("");

        foreach (var block in report.Blocks)
        {
            lines.Add(block.Ok
                ? $"  {block.Title,-10}  в порядке"
                : $"  {block.Title,-10}  ПОВРЕЖДЁН  (записано {block.Stored[..8]}, посчитано {block.Actual[..8]})");
        }

        lines.Add("");
        lines.Add(report.AllOk
            ? "Итог: файл целый, игра его загрузит."
            : $"Итог: повреждённых блоков {report.BadCount}. Пустые слоты в этом списке - это нормально,");

        if (!report.AllOk)
        {
            lines.Add("но повреждённый блок с персонажем игра покажет как \"Save data is corrupt\".");
        }

        return string.Join(Environment.NewLine, lines);
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
                "Перезаписать снимок?",
                $"Файл {fileName} уже есть в папке снимков. Его содержимое будет заменено.",
                "Перезаписать");
            if (!overwrite) { Say("Отменено", "TextSecondaryBrush"); return; }
        }

        IsBusy = true;
        Say("Делаю снимок...", "TextSecondaryBrush");
        try
        {
            var result = await _snapshotService.CreateAsync(
                SelectedSaveFile.Path, SnapshotFolder, fileName, overwrite);

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

        var targetName = SelectedSaveFile?.Name ?? "ER0000.sl2";
        var target = Path.Combine(SelectedAccount.Account.Path, targetName);

        var warning = GameSaves.IsGameRunning()
            ? "\n\nИгра сейчас запущена. Закройте её полностью: она держит сохранение в памяти и перезапишет файл своим состоянием при выходе."
            : "";

        var cloud = SelectedAccount.Account.HasSteamCloudMarker
            ? "\n\nУ аккаунта включён Steam Cloud. Если игра или Steam запущены, облако может вернуть прежнюю версию."
            : "";

        var confirmed = await ConfirmAsync(
            "Восстановить сохранение в игру?",
            $"Снимок: {SelectedSnapshot.Name}\nЗаменит файл: {target}\n\n"
            + "Текущий сейв будет сохранён в резервную копию - это делается всегда."
            + warning + cloud,
            "Восстановить");
        if (!confirmed) { Say("Отменено", "TextSecondaryBrush"); return; }

        IsBusy = true;
        Say("Восстанавливаю...", "TextSecondaryBrush");
        try
        {
            var result = await _snapshotService.RestoreAsync(SelectedSnapshot.Path, target);
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
        if (doomed.Count > shown) names += NewLine + $"  ... и ещё {doomed.Count - shown}";

        var title = doomed.Count == 1
            ? "Удалить снимок?"
            : $"Удалить {doomed.Count} {Plural(doomed.Count, "снимок", "снимка", "снимков")}?";

        var confirmed = await ConfirmAsync(
            title,
            names + NewLine + NewLine + "Файлы будут удалены с диска безвозвратно.",
            doomed.Count == 1 ? "Удалить" : $"Удалить {doomed.Count}");
        if (!confirmed) { Say("Отменено", "TextSecondaryBrush"); return; }

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
            Say(removed == 1 ? "Снимок удалён" : $"Удалено файлов: {removed}", "TextSecondaryBrush");
        }
        else
        {
            Say($"Удалено {removed}, не удалось удалить {failed.Count}: {failed[0]}", "DangerBrush");
        }

        await Task.CompletedTask;
    }

    private async Task RenameSnapshotAsync()
    {
        if (SelectedSnapshot is null || PromptAsync is null) return;

        var current = SelectedSnapshot.Name;
        var answer = await PromptAsync("Переименовать снимок", "Новое имя файла", current);
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
            "Подпись аккаунта",
            $"Как называть аккаунт {SelectedAccount.Account.SteamId}?",
            SelectedAccount.Alias ?? "");
        if (answer is null) return;

        var id = SelectedAccount.Account.SteamId;
        if (string.IsNullOrWhiteSpace(answer)) _settings.Values.Aliases.Remove(id);
        else _settings.Values.Aliases[id] = answer.Trim();
        _settings.Save();

        RefreshAccounts();
    }

    private async Task PickSnapshotFolderAsync()
    {
        if (PickFolderAsync is null) return;

        var picked = await PickFolderAsync("Куда складывать снимки", SnapshotFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        SnapshotFolder = picked;
        Log.Info("Папка снимков изменена", picked);
    }

    private async Task PickAutoFolderAsync()
    {
        if (PickFolderAsync is null) return;

        var picked = await PickFolderAsync("Куда складывать автосохранения", AutoFolder);
        if (string.IsNullOrWhiteSpace(picked)) return;

        AutoFolder = picked;
        Log.Info("Папка автосохранений изменена", picked);
    }

    private async Task ExportLogAsync()
    {
        if (SaveFileAsync is null) return;

        var path = await SaveFileAsync("Сохранить журнал", $"erdtree-keeper-log-{DateTime.Now:yyyy-MM-dd}.txt");
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await Log.ExportAsync(path);
            Say("Журнал сохранён", "FreshBrush");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say($"Не удалось сохранить журнал: {ex.Message}", "DangerBrush");
        }
    }

    private void AppendFromContext(bool useBoss)
    {
        if (SaveContext is null)
        {
            Say("Сначала прочитайте сейв", "WarnBrush");
            return;
        }

        var point = useBoss ? SaveContext.Boss : SaveContext.Location;
        if (point is null)
        {
            Say(useBoss ? "Рядом нет арены босса" : "Локация не определилась", "WarnBrush");
            return;
        }

        var name = SnapshotName;
        if (SaveContext.IsDlc) name = SnapshotNaming.EnsureDlcTag(name);
        SnapshotName = SnapshotNaming.Append(name, point.Ru);
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
        if (AutoSnapshotEnabled) TrackWritesForAutoSnapshot();
    }

    private void UpdateFreshness()
    {
        if (SelectedSaveFile is null || !File.Exists(SelectedSaveFile.Path))
        {
            FreshnessLevel = Freshness.Unknown;
            FreshnessText = "Сейв не выбран";
            return;
        }

        DateTime written;
        try { written = File.GetLastWriteTime(SelectedSaveFile.Path); }
        catch (IOException) { return; }

        var age = DateTime.Now - written;

        FreshnessText = $"Записан {written:dd.MM.yyyy HH:mm:ss}  ·  {Humanize(age)}";

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
    /// Автоснимок делается по факту записи, а не по таймеру: сначала ждём
    /// изменения файла, потом - пока он перестанет меняться. Снимок, снятый в
    /// момент записи, был бы обрывком.
    /// </summary>
    private void TrackWritesForAutoSnapshot()
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
            var context = await Task.Run(async () =>
            {
                var bytes = await Sl2File.ReadAllBytesSharedAsync(path);
                return SaveContextReader.Read(bytes);
            });

            var name = SnapshotNaming.AutoName(context, DateTime.Now, Extension);
            var result = await _snapshotService.CreateAsync(path, folder, name, overwrite: false);

            if (result.Success)
            {
                var removed = _snapshotService.Rotate(folder, AutoKeep);
                Say(removed > 0 ? $"Автоснимок: {name} (удалено старых: {removed})" : $"Автоснимок: {name}",
                    "FreshBrush");
                RefreshSnapshots();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Автоснимок не удался: {ex.Message}", path);
        }
    }

    private void ResetWriteTracking()
    {
        _pendingWrite = null;
        _lastSeenWrite = default;
        _lastSeenLength = 0;
    }

    // ─── Мелочи ─────────────────────────────────────────────────────────

    /// <summary>Перенос строки для текстов диалогов.</summary>
    private static string NewLine => Environment.NewLine;

    private void UpdateCreateAvailability() => CreateSnapshotCommand.RaiseCanExecuteChanged();

    private void Say(string message, string brushKey)
    {
        Status = message;
        StatusBrush = Brush(brushKey);
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
        if (span.TotalSeconds < 60) return $"{span.TotalSeconds:N0} сек назад";
        if (span.TotalMinutes < 60) return $"{span.TotalMinutes:N0} мин назад";
        if (span.TotalHours < 24) return $"{span.TotalHours:N0} ч назад";
        return $"{span.TotalDays:N0} дн назад";
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
