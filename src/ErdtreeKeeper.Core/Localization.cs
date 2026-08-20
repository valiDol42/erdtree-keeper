using System.ComponentModel;

namespace ErdtreeKeeper.Core;

public enum Lang { Ru, En }

/// <summary>
/// Строки интерфейса на двух языках.
///
/// Словарь в коде, а не ресурсы .resx: приложение собирается в NativeAOT, где
/// сателлитные сборки с переводом - лишний источник хлопот, а строки всё равно
/// нужны и в Core (сообщения об ошибках), и в разметке.
///
/// Разметка обращается через индексатор: Loc.Current["ключ"]. При смене языка
/// поднимается уведомление об изменении индексатора, и весь интерфейс
/// перерисовывается без перезапуска.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Current { get; } = new();

    private Lang _language = Lang.Ru;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Lang Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;

            // "Item[]" - имя, по которому WPF/Avalonia обновляют все привязки
            // к индексатору разом.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnglish)));
        }
    }

    public bool IsEnglish => _language == Lang.En;

    public string this[string key] => Get(key);

    /// <summary>Строка по ключу. Неизвестный ключ возвращается как есть - это заметно.</summary>
    public static string Get(string key) => Get(Current._language, key);

    /// <summary>
    /// Строка на заданном языке, не трогая текущий.
    ///
    /// Нужна там, где язык ещё не выбран приложением: настройки выводят из
    /// него имя папки по умолчанию, но переключать язык всему окну чтение
    /// файла не должно.
    /// </summary>
    public static string Get(Lang language, string key) =>
        Table.TryGetValue(key, out var pair)
            ? (language == Lang.En ? pair.En : pair.Ru)
            : key;

    /// <summary>
    /// Все строки одного языка отдельным словарём.
    ///
    /// Разметка привязывается к снимку, а не к самой таблице: обновить
    /// привязку к индексатору уведомлением "Item[]" не выходит, а на подмену
    /// целого словаря привязки реагируют надёжно.
    /// </summary>
    public static Dictionary<string, string> Snapshot() => Snapshot(Current._language);

    public static Dictionary<string, string> Snapshot(Lang language) =>
        Table.ToDictionary(pair => pair.Key, pair => language == Lang.En ? pair.Value.En : pair.Value.Ru);

    /// <summary>
    /// Форма слова по числу. В таблице лежит "один|два|много".
    ///
    /// У русского три формы и исключение на 11-14, у английского две -
    /// поэтому правило зависит от языка, а не от одного шаблона.
    /// </summary>
    public static string Plural(int count, string key)
    {
        var forms = Get(key).Split('|');
        if (forms.Length < 3) return forms[0];

        if (Current._language == Lang.En) return count == 1 ? forms[0] : forms[1];

        if (count % 100 is >= 11 and <= 14) return forms[2];
        return (count % 10) switch
        {
            1 => forms[0],
            2 or 3 or 4 => forms[1],
            _ => forms[2],
        };
    }

    /// <summary>Строка с подстановкой.</summary>
    public static string Get(string key, params object[] args) =>
        string.Format(Get(key), args);

    /// <summary>
    /// Язык по умолчанию берётся из системы: русскоязычному игроку не нужно
    /// искать переключатель, англоязычному - тем более.
    /// </summary>
    public static Lang DetectFromSystem()
    {
        // Приложение собирается с InvariantGlobalization: библиотека ICU в него
        // не входит, и CurrentUICulture всегда инвариантна - язык системы через
        // неё не виден вообще. На Windows его отдаёт сама система, и это
        // единственный способ узнать его в такой сборке.
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return FromWindowsLanguageId(GetUserDefaultUILanguage());
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                // Дальше по общему пути.
            }
        }

        try
        {
            return FromCultureName(System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException)
        {
            return Lang.En;
        }
    }

    /// <summary>Кириллические локали ближе к русскому переводу, чем к английскому.</summary>
    public static Lang FromCultureName(string twoLetterName) =>
        twoLetterName is "ru" or "uk" or "be" or "kk" ? Lang.Ru : Lang.En;

    /// <summary>
    /// Язык по идентификатору Windows. Младшие десять бит - основной язык:
    /// 0x19 русский, 0x22 украинский, 0x23 белорусский, 0x3F казахский.
    /// </summary>
    public static Lang FromWindowsLanguageId(int languageId) =>
        (languageId & 0x3FF) is 0x19 or 0x22 or 0x23 or 0x3F ? Lang.Ru : Lang.En;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    private static readonly Dictionary<string, (string Ru, string En)> Table = new()
    {
        // ─── Шапка и общее ──────────────────────────────────────────────
        ["app.subtitle"] = ("Хранитель файлов сохранения Elden Ring", "Elden Ring save file keeper"),
        ["app.whatItDoes"] = ("Что программа делает", "What it does"),
        ["app.about"] = ("О программе", "About"),
        ["app.close"] = ("Закрыть", "Close"),
        ["app.cancel"] = ("Отмена", "Cancel"),
        ["app.done"] = ("Готово", "Done"),
        ["app.save"] = ("Сохранить", "Save"),
        ["app.browse"] = ("Обзор...", "Browse..."),
        ["app.openFolder"] = ("Открыть папку", "Open folder"),
        ["app.language"] = ("Язык", "Language"),

        // ─── Баннер трекера ─────────────────────────────────────────────
        ["tracker.name"] = ("Erdtree Compass", "Erdtree Compass"),
        ["tracker.tagline"] = ("интерактивная карта Elden Ring", "interactive Elden Ring map"),
        ["tracker.safe"] = ("Удобно и абсолютно безопасно!", "Handy and completely safe!"),
        ["tracker.openTip"] = ("Открыть eldenring.krut.top в браузере", "Open eldenring.krut.top in your browser"),
        ["tracker.pitch"] = (
            "{0} {1} благодати и {2} {3} на карте. Прогресс подтягивается из вашего сохранения.",
            "{0} {1} of grace and {2} {3} on the map. Progress is read from your save."),

        // ─── Откуда ─────────────────────────────────────────────────────
        ["source.section"] = ("ОТКУДА", "SOURCE"),
        ["source.account"] = ("Аккаунт Steam", "Steam account"),
        ["source.rename"] = ("Подписать", "Label"),
        ["source.file"] = ("Файл сохранения", "Save file"),
        ["source.read"] = ("Прочитать сейв", "Read save"),
        ["source.checkIntegrity"] = ("Проверить целостность", "Check integrity"),
        ["source.readHint"] = (
            "Нажмите \"Прочитать сейв\", чтобы увидеть персонажа",
            "Press \"Read save\" to see the character"),
        ["source.playerCardTip"] = ("Открыть карточку игрока", "Open the character sheet"),

        // ─── Свежесть ───────────────────────────────────────────────────
        ["fresh.none"] = ("Сейв не выбран", "No save selected"),
        ["fresh.written"] = ("Записан {0}  ·  {1}", "Written {0}  ·  {1}"),
        ["fresh.secondsAgo"] = ("{0} сек назад", "{0}s ago"),
        ["fresh.minutesAgo"] = ("{0} мин назад", "{0}m ago"),
        ["fresh.hoursAgo"] = ("{0} ч назад", "{0}h ago"),
        ["fresh.daysAgo"] = ("{0} дн назад", "{0}d ago"),

        // ─── Имя снимка ─────────────────────────────────────────────────
        ["name.section"] = ("ИМЯ СНИМКА", "SNAPSHOT NAME"),
        ["name.placeholder"] = ("например: Годрик", "for example: Godrick"),
        ["name.addLocation"] = ("+ локация", "+ location"),
        ["name.addBoss"] = ("+ босс", "+ boss"),
        ["name.addBefore"] = ("+ до", "+ before"),
        ["name.addAfter"] = ("+ после", "+ after"),
        ["name.clear"] = ("очистить", "clear"),
        ["name.addLocationTip"] = (
            "Подставить ближайшее место благодати из сейва",
            "Insert the nearest site of grace from the save"),
        ["name.addBossTip"] = (
            "Подставить ближайшую арену босса из сейва",
            "Insert the nearest boss arena from the save"),
        ["name.enter"] = ("Введите имя снимка", "Enter a snapshot name"),
        ["name.willCreate"] = ("Будет создан: {0}", "Will create: {0}"),
        ["name.willOverwrite"] = ("Будет перезаписан: {0}", "Will overwrite: {0}"),
        ["name.take"] = ("Сделать снимок", "Take snapshot"),

        // ─── Автосохранение ─────────────────────────────────────────────
        ["auto.enable"] = ("Автосохранение", "Autosave"),
        ["auto.configure"] = ("Настроить...", "Configure..."),
        ["auto.enableTip"] = (
            "Снимок делается после того, как игра запишет сейв. Смотреть и восстанавливать их можно, переключив список справа на «Автосохранения»",
            "A snapshot is taken once the game has written the save. Switch the list on the right to \"Autosaves\" to see and restore them"),
        ["auto.title"] = ("Автосохранение", "Autosave"),
        ["auto.explain"] = (
            "Снимок делается по факту записи сейва игрой, а не по расписанию: копия, снятая в момент записи, была бы обрывком. Указанные минуты - нижняя граница частоты, чаще этого снимок не сработает.",
            "A snapshot is taken when the game finishes writing the save, not on a schedule: a copy caught mid-write would be half a file. The minutes below are a floor on frequency, not a timer."),
        ["auto.interval"] = ("Не чаще одного снимка в", "No more than one snapshot every"),
        ["auto.minutes"] = ("мин", "min"),
        ["auto.keep"] = ("Хранить последних", "Keep the most recent"),
        ["auto.pieces"] = ("шт", "files"),
        ["auto.folder"] = ("ПАПКА ДЛЯ АВТОСОХРАНЕНИЙ", "AUTOSAVE FOLDER"),
        ["auto.folderHint"] = (
            "Можно указать другой диск - тогда копии переживут переустановку системы.",
            "Point it at another drive and the copies will survive a reinstall."),

        // ─── Список снимков ─────────────────────────────────────────────
        ["list.manual"] = ("Снимки", "Snapshots"),
        ["list.auto"] = ("Автосохранения", "Autosaves"),
        ["list.name"] = ("Имя", "Name"),
        ["list.modified"] = ("Изменён", "Modified"),
        ["list.empty"] = ("Снимков пока нет", "No snapshots yet"),
        ["list.emptyManual"] = (
            "Сядьте у благодати, чтобы игра записала сейв, задайте имя слева и нажмите «Сделать снимок».",
            "Rest at a site of grace so the game writes the save, name it on the left and press \"Take snapshot\"."),
        ["list.emptyAuto"] = (
            "Включите автосохранение слева. Снимок появится здесь после того, как игра запишет сейв.",
            "Turn autosave on at the left. Snapshots appear here once the game writes the save."),
        ["list.selectAll"] = ("Выделить всё", "Select all"),
        ["list.clearSelection"] = ("Снять выделение", "Clear selection"),
        ["list.nothingSelected"] = ("ничего не выбрано", "nothing selected"),
        ["list.oneSelected"] = ("выбран 1 файл из {0}", "1 file of {0} selected"),
        ["list.manySelected"] = ("выбрано {0} {1} из {2}", "{0} {1} of {2} selected"),
        ["list.rename"] = ("Переименовать", "Rename"),
        ["list.delete"] = ("Удалить", "Delete"),
        ["list.deleteCount"] = ("Удалить ({0})", "Delete ({0})"),
        ["list.restore"] = ("Восстановить в игру", "Restore to game"),
        ["list.oneOnlyTip"] = (
            "Доступно, когда выбран ровно один файл",
            "Available when exactly one file is selected"),


        // ─── Первый запуск ──────────────────────────────────────────────
        ["onboard.title"] = ("Прежде чем начать", "Before you start"),
        ["onboard.lead"] = (
            "Эта программа копирует файлы сохранений Elden Ring, чтобы к ним можно было вернуться. Она открытая: исходный код можно прочитать целиком, и ниже честно написано, что именно она делает.",
            "This program copies Elden Ring save files so you can go back to them. It is open source: you can read all of it, and below is a plain account of what it does."),
        ["onboard.madeBy"] = ("Её делает команда карты", "Made by the team behind"),
        ["onboard.does"] = ("ЧТО ОНА ДЕЛАЕТ", "WHAT IT DOES"),
        ["onboard.does1"] = (
            "•  Читает файлы сохранений и копирует их в выбранную вами папку.",
            "•  Reads save files and copies them to a folder you choose."),
        ["onboard.does2"] = (
            "•  По вашей команде кладёт копию обратно в папку игры, предварительно сохранив текущий сейв.",
            "•  On your command puts a copy back into the game folder, backing up the current save first."),
        ["onboard.does3"] = (
            "•  Хранит настройки в одном файле рядом с собой.",
            "•  Keeps its settings in a single file next to itself."),
        ["onboard.doesNot"] = ("ЧЕГО ОНА НЕ ДЕЛАЕТ", "WHAT IT DOES NOT DO"),
        ["onboard.not1"] = (
            "•  Не выходит в интернет: ни обновлений, ни статистики, ни отправки файлов. Ссылка выше открывает сайт в вашем браузере - соединение устанавливает он, а не программа.",
            "•  Does not go online: no updates, no telemetry, no uploads. The link above opens your browser - the browser makes the connection, not this program."),
        ["onboard.not2"] = (
            "•  Не меняет ни одного байта внутри сохранения - только копирует файл целиком.",
            "•  Does not change a single byte inside a save - it copies the whole file."),
        ["onboard.not3"] = (
            "•  Не просит прав администратора и не трогает системные настройки.",
            "•  Does not ask for administrator rights and does not touch system settings."),
        ["onboard.not4"] = (
            "•  Не запускается сама и ничего не прописывает в автозагрузку.",
            "•  Does not start by itself and adds nothing to startup."),
        ["onboard.warning"] = (
            "Восстановление заменяет сейв в игре. Программа всегда сначала откладывает текущий файл в резервную копию, но игру перед этим нужно полностью закрыть - иначе она перезапишет файл своим состоянием.",
            "Restoring replaces the save in the game. The current file is always backed up first, but close the game completely beforehand - otherwise it will write its own state over the file."),
        ["onboard.ok"] = ("Понятно", "Got it"),

        // ─── Что программа делает ───────────────────────────────────────
        ["trans.title"] = ("Что программа делает", "What this program does"),
        ["trans.noNetwork"] = ("Не выходит в интернет", "Does not go online"),
        ["trans.noNetworkWhy"] = (
            "В готовом файле нет ни одной сетевой библиотеки - открыть соединение программа технически не может.",
            "The built binary imports no networking library - opening a connection is not something it can do."),
        ["trans.reads"] = ("Читает", "Reads"),
        ["trans.readsWhat"] = (
            "Папку сохранений {0} и файлы .sl2 и .co2 внутри неё. Файлы открываются только на чтение и в режиме, который не мешает игре.",
            "The save folder {0} and the .sl2 and .co2 files inside it. Files are opened read-only, in a sharing mode that does not disturb the game."),
        ["trans.writes"] = ("Пишет", "Writes"),
        ["trans.writesWhat"] = (
            "Снимки в папку {0}\nНастройки в файл {1}\n\nВ папку игры программа пишет ровно один раз - когда вы нажимаете \"Восстановить в игру\". Перед этим текущий сейв всегда уезжает в подпапку \"{2}\".",
            "Snapshots into {0}\nSettings into {1}\n\nIt writes into the game folder exactly once - when you press \"Restore to game\". The current save always goes into the \"{2}\" subfolder first."),
        ["trans.notDoes"] = ("Не делает", "Does not"),
        ["trans.notDoesWhat"] = (
            "Не обращается в интернет - ни за обновлениями, ни со статистикой. Ссылка на сайт проекта - единственное исключение, и работает она не сама: по щелчку программа просит систему открыть браузер, а соединение устанавливает уже он.\nНе меняет содержимое сохранений: копируется файл целиком, байт в байт.\nНе просит прав администратора.\nНе прописывается в автозагрузку и не остаётся в памяти после закрытия.\nНе изменяет файлы игры, реестр и системные настройки.",
            "Does not reach the internet - no update checks, no telemetry. The project link is the only exception, and it does not act on its own: on a click the program asks the system to open a browser, which then makes the connection.\nDoes not alter save contents: the file is copied whole, byte for byte.\nDoes not ask for administrator rights.\nDoes not add itself to startup and does not stay in memory after closing.\nDoes not modify game files, the registry or system settings."),
        ["trans.howToCheck"] = ("Как проверить", "How to check"),
        ["trans.howToCheckWhat"] = (
            "Журнал операций показывает каждое обращение к диску, и его можно выгрузить в текстовый файл.\nОтсутствие сети проверяется любым монитором соединений - в готовом файле нет ни одной сетевой библиотеки.\nИсходный код открыт: собранный файл можно сверить по контрольной сумме в окне \"О программе\".",
            "The activity log shows every disk access and exports to a text file.\nThe absence of networking can be checked with any connection monitor - the binary imports no networking library.\nThe source is open: the built file can be checked against the hash shown in the \"About\" window."),

        // ─── О программе ────────────────────────────────────────────────
        ["about.version"] = (
            "Версия {0}. Хранитель файлов сохранения Elden Ring.\nЛицензия MIT.",
            "Version {0}. Elden Ring save file keeper.\nMIT License."),
        ["about.site"] = ("САЙТ ПРОЕКТА", "PROJECT SITE"),
        ["about.siteWhat"] = (
            "Карта и трекер прогресса Elden Ring. Оттуда же взят справочник мест благодати и арен боссов, по которому программа называет снимки.",
            "An Elden Ring map and progress tracker. The reference of graces and boss arenas that names snapshots comes from there."),
        ["about.source"] = ("ИСХОДНЫЙ КОД", "SOURCE CODE"),
        ["about.sourceWhat"] = (
            "Всё, что делает программа, можно прочитать целиком. Там же лежат контрольные суммы релизов и подтверждение того, что выложенный файл собран именно из этого кода.",
            "Everything the program does can be read in full. Release checksums live there too, along with the proof that the published file was built from this code."),
        ["about.contact"] = ("СВЯЗЬ", "CONTACT"),
        ["about.contactWhat"] = (
            "Вопросы, ошибки и всё, что касается безопасности - сюда.",
            "Questions, bugs and anything about security - here."),
        ["about.where"] = ("ГДЕ ЧТО ЛЕЖИТ", "WHERE THINGS ARE"),
        ["about.whereWhat"] = (
            "Программа: {0}\n\nНастройки: {1}\nСейчас на диске: {2}\n\n{3}",
            "Program: {0}\n\nSettings: {1}\nOn disk now: {2}\n\n{3}"),
        ["about.portable"] = (
            "Настройки лежат рядом с программой - её можно носить на флешке.",
            "Settings sit next to the program - you can carry it on a flash drive."),
        ["about.notPortable"] = (
            "Папка программы недоступна для записи, поэтому настройки ушли в AppData.",
            "The program folder is not writable, so settings went to AppData."),
        ["about.hash"] = ("КОНТРОЛЬНАЯ СУММА ЭТОГО ФАЙЛА", "CHECKSUM OF THIS FILE"),
        ["about.hashWhy"] = (
            "Сверьте её с суммой, опубликованной на странице релиза: совпадение означает, что файл не подменяли.",
            "Compare it with the hash published on the release page: a match means the file was not tampered with."),
        ["about.hashButton"] = ("Посчитать SHA-256", "Compute SHA-256"),
        ["about.hashPrompt"] = ("нажмите кнопку, чтобы посчитать", "press the button to compute"),
        ["about.hashWorking"] = ("считаю...", "computing..."),

        // ─── Карточка игрока ────────────────────────────────────────────
        ["card.title"] = ("Карточка игрока", "Character sheet"),
        ["card.levelLine"] = ("{0} уровень  ·  {1}  ·  слот {2}", "level {0}  ·  {1}  ·  slot {2}"),
        ["card.vitals"] = ("ПОКАЗАТЕЛИ", "VITALS"),
        ["card.hp"] = ("Очки здоровья", "HP"),
        ["card.fp"] = ("Очки фокуса", "FP"),
        ["card.stamina"] = ("Очки выносливости", "Stamina"),
        ["card.progress"] = ("ПРОГРЕСС", "PROGRESS"),
        ["card.runes"] = ("Рун при себе", "Runes held"),
        ["card.runeMemory"] = ("Собрано рун всего", "Runes earned in total"),
        ["card.played"] = ("В игре", "Time played"),
        ["card.dlc"] = ("ЗЕМЛИ ТЕНЕЙ", "SHADOW OF THE ERDTREE"),
        ["card.scadutree"] = ("Благословение Древа Теней", "Scadutree Blessing"),
        ["card.spiritAsh"] = ("Благословение праха", "Revered Spirit Ash Blessing"),
        ["card.where"] = ("ГДЕ СЕЙЧАС", "WHEREABOUTS"),
        ["card.nearestGrace"] = ("Ближайшая благодать", "Nearest site of grace"),
        ["card.nearestBoss"] = ("Ближайший босс", "Nearest boss"),
        ["card.map"] = ("Карта", "Map"),
        ["card.unknown"] = ("неизвестно", "unknown"),
        ["card.noneNearby"] = ("рядом нет", "none nearby"),
        ["card.notFound"] = ("не определилась", "not determined"),
        ["card.distance"] = ("{0} ({1} м)", "{0} ({1} m)"),
        ["card.levelMismatch"] = (
            "Уровень не сходится с характеристиками: в сейве {0}, по вложенным очкам {1}. Числам выше верить нельзя.",
            "Level does not match the stats: {0} in the save, {1} from the points spent. Do not trust the numbers above."),

        // ─── Характеристики ─────────────────────────────────────────────
        ["stat.vigor"] = ("Здоровье", "Vigor"),
        ["stat.mind"] = ("Внимание", "Mind"),
        ["stat.endurance"] = ("Выносливость", "Endurance"),
        ["stat.strength"] = ("Сила", "Strength"),
        ["stat.dexterity"] = ("Ловкость", "Dexterity"),
        ["stat.intelligence"] = ("Интеллект", "Intelligence"),
        ["stat.faith"] = ("Вера", "Faith"),
        ["stat.arcane"] = ("Мистицизм", "Arcane"),

        // ─── Классы ─────────────────────────────────────────────────────
        ["class.0"] = ("Бродяга", "Vagabond"),
        ["class.1"] = ("Воин", "Warrior"),
        ["class.2"] = ("Герой", "Hero"),
        ["class.3"] = ("Разбойник", "Bandit"),
        ["class.4"] = ("Астролог", "Astrologer"),
        ["class.5"] = ("Пророк", "Prophet"),
        ["class.6"] = ("Самурай", "Samurai"),
        ["class.7"] = ("Узник", "Prisoner"),
        ["class.8"] = ("Исповедник", "Confessor"),
        ["class.9"] = ("Бедняга", "Wretch"),


        // ─── Разное ─────────────────────────────────────────────────────
        ["time.hours"] = ("ч", "h"),
        ["ctx.nowhere"] = ("неизвестно где", "location unknown"),
        ["ctx.near"] = ("рядом: {0} ({1} м)", "near: {0} ({1} m)"),
        ["ctx.noBoss"] = ("боссов рядом нет", "no boss nearby"),
        ["ctx.boss"] = ("босс: {0} ({1} м)", "boss: {0} ({1} m)"),
        ["ctx.dlc"] = ("Земли Теней", "Shadow of the Erdtree"),


        // ─── Состояние и диалоги ────────────────────────────────────────
        ["status.ready"] = ("Готово", "Ready"),
        ["status.listRefreshed"] = ("Список обновлён", "List refreshed"),
        ["status.reading"] = ("Читаю сейв...", "Reading the save..."),
        ["status.saveRead"] = ("Сейв прочитан", "Save read"),
        ["status.noCharacters"] = ("Персонажи в сейве не найдены", "No characters found in the save"),
        ["status.checking"] = ("Проверяю целостность...", "Checking integrity..."),
        ["status.snapshotting"] = ("Делаю снимок...", "Taking the snapshot..."),
        ["status.restoring"] = ("Восстанавливаю...", "Restoring..."),
        ["status.cancelled"] = ("Отменено", "Cancelled"),
        ["status.logSaved"] = ("Журнал сохранён", "Log saved"),
        ["status.nameCleared"] = ("Имя очищено", "Name cleared"),
        ["status.readFirst"] = ("Сначала прочитайте сейв", "Read the save first"),
        ["status.noBossNearby"] = ("Рядом нет арены босса", "No boss arena nearby"),
        ["status.noLocation"] = ("Локация не определилась", "Location could not be determined"),
        ["status.integrityOk"] = (
            "Сейв целый: все контрольные суммы сошлись",
            "The save is intact: every checksum matched"),
        ["status.damagedBlocks"] = ("Повреждённых блоков: {0}", "Damaged blocks: {0}"),
        ["status.pickFolder"] = (
            "Выберите папку назначения - она запомнится",
            "Choose a destination folder - it will be remembered"),
        ["status.noSaveFolder"] = ("Папка сохранений не найдена: {0}", "Save folder not found: {0}"),
        ["status.gameFolderRejected"] = (
            "Это папка сохранений игры. Выберите другую - иначе снимки смешаются с сейвами",
            "That is the game save folder. Pick another one, or snapshots will mix with saves"),
        ["log.accountsFound"] = ("Найдено аккаунтов: {0}", "Accounts found: {0}"),
        ["dlg.pickSnapshotFolder"] = ("Куда складывать снимки", "Where to keep snapshots"),
        ["dlg.pickAutoFolder"] = ("Куда складывать автосохранения", "Where to keep autosaves"),
        ["dlg.saveLog"] = ("Сохранить журнал", "Save the log"),
        ["dlg.renameTitle"] = ("Переименовать снимок", "Rename snapshot"),
        ["dlg.renameBody"] = ("Новое имя файла", "New file name"),
        ["dlg.aliasTitle"] = ("Подпись аккаунта", "Account label"),
        ["dlg.aliasBody"] = ("Как называть аккаунт {0}?", "What should account {0} be called?"),
        ["dlg.integrityTitle"] = ("Проверка целостности", "Integrity check"),
        ["dlg.overwrite"] = ("Перезаписать", "Overwrite"),
        ["dlg.overwriteTitle"] = ("Перезаписать снимок?", "Overwrite the snapshot?"),
        ["dlg.overwriteBody"] = (
            "Файл {0} уже есть в папке снимков. Его содержимое будет заменено.",
            "The file {0} already exists in the snapshots folder. Its contents will be replaced."),
        ["dlg.deleteOneTitle"] = ("Удалить снимок?", "Delete the snapshot?"),
        ["dlg.deleteManyTitle"] = ("Удалить {0} {1}?", "Delete {0} {1}?"),
        ["dlg.deleteBody"] = (
            "Файлы будут удалены с диска безвозвратно.",
            "The files will be deleted from disk permanently."),
        ["dlg.restoreTitle"] = ("Восстановить сохранение в игру?", "Restore the save to the game?"),
        ["dlg.restoreBody"] = (
            "Снимок: {0}\nЗаменит файл: {1}\n\nТекущий сейв будет сохранён в резервную копию - это делается всегда.",
            "Snapshot: {0}\nWill replace: {1}\n\nThe current save is always backed up first."),
        ["dlg.restoreGameRunning"] = (
            "\n\nИгра сейчас запущена. Закройте её полностью: она держит сохранение в памяти и перезапишет файл своим состоянием при выходе.",
            "\n\nThe game is running. Close it completely: it holds the save in memory and will write its own state over the file on exit."),
        ["dlg.restoreCloud"] = (
            "\n\nУ аккаунта включён Steam Cloud. Если игра или Steam запущены, облако может вернуть прежнюю версию.",
            "\n\nSteam Cloud is on for this account. If the game or Steam is running, the cloud may restore the previous version."),
        ["list.restoreVerb"] = ("Восстановить", "Restore"),
        ["auto.fallbackName"] = ("автоснимок", "autosave"),
        ["plural.file"] = ("файл|файла|файлов", "file|files|files"),
        ["plural.snapshot"] = ("снимок|снимка|снимков", "snapshot|snapshots|snapshots"),
        ["plural.place"] = ("место|места|мест", "site|sites|sites"),
        ["plural.boss"] = ("босс|босса|боссов", "boss|bosses|bosses"),

        ["card.levelShort"] = ("{0} ур.", "level {0}"),

        ["about.title"] = ("О программе", "About"),
        ["about.hashNoPath"] = ("не удалось определить путь к файлу", "could not determine the file path"),
        ["about.hashFailed"] = ("не удалось посчитать: {0}", "could not compute: {0}"),
        ["op.sourceMissing"] = ("Файл сохранения не найден", "Save file not found"),
        ["op.badName"] = ("В имени файла есть недопустимые символы", "The file name contains invalid characters"),
        ["op.nameTaken"] = ("Файл с таким именем уже есть", "A file with that name already exists"),
        ["op.snapshotMissing"] = ("Снимок не найден", "Snapshot not found"),
        ["op.created"] = ("Снимок создан и проверен", "Snapshot created and verified"),
        ["op.copyMismatch"] = (
            "Копия не совпала с оригиналом - попробуйте ещё раз",
            "The copy did not match the original - try again"),
        ["op.gameWriting"] = (
            "Игра записывала сохранение прямо сейчас - подождите пару секунд и повторите",
            "The game was writing the save just now - wait a couple of seconds and retry"),
        ["op.deleted"] = ("Снимок удалён", "Snapshot deleted"),
        ["op.noPreviousFile"] = ("(файла не было)", "(there was no file)"),

        ["app.build"] = (
            "{0} (сборка {1})",
            "{0} (build {1})"),
        ["startup.missingTitle"] = (
            "{0}: не хватает файлов",
            "{0}: files are missing"),
        ["startup.missingBody"] = (
            "Рядом с программой не хватает файлов:\n\n{0}\n\nСкорее всего, из архива распакован только ErdtreeKeeper.exe. Распакуйте архив целиком - библиотеки должны лежать в одной папке с программой.\n\nПапка: {1}",
            "Files are missing next to the program:\n\n{0}\n\nMost likely only ErdtreeKeeper.exe was extracted from the archive. Extract the whole archive - the libraries have to sit in the same folder as the program.\n\nFolder: {1}"),
        ["err.crashTitle"] = (
            "Что-то пошло не так",
            "Something went wrong"),
        ["err.crashBody"] = (
            "Действие не выполнено. Файлы не тронуты.",
            "The action did not go through. No files were touched."),
        ["err.crashLog"] = (
            "Сбой: {0}: {1}",
            "Failure: {0}: {1}"),
        ["auto.pickFolder"] = (
            "Куда складывать автосохранения",
            "Where to keep autosaves"),
        ["log.read"] = (
            "чтение",
            "read"),
        ["log.write"] = (
            "запись",
            "write"),
        ["log.delete"] = (
            "удаление",
            "delete"),
        ["log.warn"] = (
            "внимание",
            "warning"),
        ["log.error"] = (
            "ошибка",
            "error"),
        ["log.info"] = (
            "инфо",
            "info"),
        ["log.exportTitle"] = (
            "Журнал операций Erdtree Keeper",
            "Erdtree Keeper activity log"),
        ["log.exportedAt"] = (
            "Выгружен: {0}",
            "Exported: {0}"),
        ["file.exists"] = (
            "файл есть: {0} байт, изменён {1}",
            "file present: {0} bytes, modified {1}"),
        ["file.missing"] = (
            "файла ещё нет - он появится, когда вы что-нибудь измените",
            "no file yet - it appears once you change something"),
        ["file.unreadable"] = (
            "не удалось прочитать: {0}",
            "could not read: {0}"),
        ["file.noAccess"] = (
            "нет доступа к файлу",
            "no access to the file"),
        ["path.snapshots"] = (
            "Снимки",
            "Snapshots"),
        ["path.restoreBackup"] = (
            "Перед восстановлением",
            "Before restore"),
        ["block.profile"] = (
            "Профиль",
            "Profile"),
        ["block.slot"] = (
            "Слот {0}",
            "Slot {0}"),
        ["problem.notEldenRing"] = (
            "это не файл сохранения Elden Ring: нет подписи BND4",
            "this is not an Elden Ring save file: no BND4 signature"),
        ["problem.truncated"] = (
            "файл обрезан: найдено блоков {0} из {1}",
            "the file is truncated: {0} of {1} blocks found"),
        ["problem.damaged"] = (
            "повреждённых блоков: {0}",
            "damaged blocks: {0}"),
        ["unit.mb"] = (
            "МБ",
            "MB"),
        ["unit.kb"] = (
            "КБ",
            "KB"),
        ["err.createFolder"] = (
            "Не удалось создать папку: {0}",
            "Could not create the folder: {0}"),
        ["err.createSnapshot"] = (
            "Не удалось создать снимок: {0}",
            "Could not create the snapshot: {0}"),
        ["err.restore"] = (
            "Не удалось восстановить: {0}",
            "Could not restore: {0}"),
        ["err.rename"] = (
            "Не удалось переименовать: {0}",
            "Could not rename: {0}"),
        ["err.delete"] = (
            "Не удалось удалить: {0}",
            "Could not delete: {0}"),
        ["err.readSave"] = (
            "Не удалось прочитать сейв: {0}",
            "Could not read the save: {0}"),
        ["err.check"] = (
            "Не удалось проверить: {0}",
            "Could not check: {0}"),
        ["err.logSave"] = (
            "Не удалось сохранить журнал: {0}",
            "Could not save the log: {0}"),
        ["err.autoSnap"] = (
            "Автоснимок не удался: {0}",
            "Autosnapshot failed: {0}"),
        ["log.readingSave"] = (
            "Читаю сохранение",
            "Reading the save"),
        ["log.copyMismatch"] = (
            "Копия не совпала с оригиналом, файл удалён",
            "The copy did not match the original, the file was deleted"),
        ["log.gameWriting"] = (
            "Игра записывала сейв во время копирования, снимок отменён",
            "The game was writing the save while copying, the snapshot was cancelled"),
        ["log.snapshotCreated"] = (
            "Снимок создан ({0} МБ)",
            "Snapshot created ({0} MB)"),
        ["log.badSnapshot"] = (
            "Снимок не годится: {0}",
            "The snapshot is not usable: {0}"),
        ["log.backupMismatch"] = (
            "Резервная копия не совпала с оригиналом, восстановление отменено",
            "The backup did not match the original, the restore was cancelled"),
        ["log.backupDone"] = (
            "Текущий сейв сохранён в резервную копию",
            "The current save was backed up"),
        ["log.stagingMismatch"] = (
            "Подготовленный файл не совпал со снимком, игровой сейв не тронут",
            "The staged file did not match the snapshot, the game save was left alone"),
        ["log.restored"] = (
            "Снимок восстановлен в игру",
            "The snapshot was restored into the game"),
        ["log.rotated"] = (
            "Старое автосохранение удалено",
            "An old autosave was deleted"),
        ["log.settingsPath"] = (
            "Настройки: {0}",
            "Settings: {0}"),
        ["log.settingsInAppData"] = (
            "Папка программы недоступна для записи, настройки лежат в AppData",
            "The program folder is not writable, so settings live in AppData"),
        ["log.autoOn"] = (
            "Автоснимки включены: снимок делается после того, как игра запишет сейв",
            "Autosnapshots on: a snapshot is taken after the game writes the save"),
        ["log.autoOff"] = (
            "Автоснимки выключены",
            "Autosnapshots off"),
        ["log.noSaves"] = (
            "Сохранения Elden Ring не найдены",
            "No Elden Ring saves found"),
        ["log.parsingSave"] = (
            "Разбираю сохранение",
            "Parsing the save"),
        ["log.integrityOk"] = (
            "Целостность в порядке",
            "Integrity is fine"),
        ["log.snapFolderChanged"] = (
            "Папка снимков изменена",
            "The snapshot folder was changed"),
        ["log.autoFolderChanged"] = (
            "Папка автосохранений изменена",
            "The autosave folder was changed"),
        ["log.gameFolderRejected"] = (
            "Выбрана папка игры - отклонено",
            "The game folder was picked - rejected"),
        ["op.createdDamaged"] = (
            "Снимок создан, но в исходном сейве повреждённых блоков: {0}",
            "Snapshot created, but the source save has damaged blocks: {0}"),
        ["op.badSnapshot"] = (
            "Снимок не годится - {0}. Восстановление отменено.",
            "The snapshot is not usable - {0}. Restore cancelled."),
        ["op.backupFailed"] = (
            "Не удалось сделать надёжную резервную копию текущего сейва - восстановление отменено",
            "Could not make a reliable backup of the current save - restore cancelled"),
        ["op.writeFailed"] = (
            "Запись не удалась, игровой сейв остался прежним. Резервная копия: {0}",
            "The write failed, the game save is unchanged. Backup: {0}"),
        ["op.restored"] = (
            "Восстановлено. Прежний сейв: {0}",
            "Restored. Previous save: {0}"),
        ["op.renamed"] = (
            "Переименовано в {0}",
            "Renamed to {0}"),
        ["warn.steamCloud"] = (
            "У этого аккаунта включена синхронизация Steam Cloud. Восстанавливайте сейв только при полностью закрытой игре, иначе Steam может вернуть облачную версию.",
            "Steam Cloud sync is on for this account. Restore only with the game fully closed, otherwise Steam may bring the cloud version back."),
        ["warn.gameRunning"] = (
            "Игра сейчас запущена. Закройте её полностью: она держит сохранение в памяти и перезапишет файл своим состоянием при выходе.",
            "The game is running right now. Close it completely: it holds the save in memory and will overwrite the file with its own state on exit."),
        ["warn.cloudRestore"] = (
            "У аккаунта включён Steam Cloud. Если игра или Steam запущены, облако может вернуть прежнюю версию.",
            "Steam Cloud is on for this account. If the game or Steam is running, the cloud may bring the previous version back."),
        ["report.file"] = (
            "Файл: {0}",
            "File: {0}"),
        ["report.notEldenRing"] = (
            "Это не похоже на сохранение Elden Ring: нет подписи BND4 в начале файла.",
            "This does not look like an Elden Ring save: no BND4 signature at the start of the file."),
        ["report.sizeNormal"] = (
            "Размер: {0} байт - как у обычного сейва.",
            "Size: {0} bytes - the same as a normal save."),
        ["report.sizeOdd"] = (
            "Размер: {0} байт - отличается от обычных {1}.",
            "Size: {0} bytes - differs from the usual {1}."),
        ["report.howItWorks1"] = (
            "Игра проверяет каждый блок по контрольной сумме MD5 и отказывается",
            "The game checks every block against an MD5 checksum and refuses to load"),
        ["report.howItWorks2"] = (
            "загружать блок, если сумма не сошлась. Ниже - результат по каждому.",
            "a block whose checksum does not match. Below is the result for each one."),
        ["report.blockOk"] = (
            "в порядке",
            "fine"),
        ["report.blockBad"] = (
            "ПОВРЕЖДЁН  (записано {0}, посчитано {1})",
            "DAMAGED  (stored {0}, computed {1})"),
        ["report.verdictOk"] = (
            "Итог: файл целый, игра его загрузит.",
            "Verdict: the file is intact, the game will load it."),
        ["report.verdictBad"] = (
            "Итог: повреждённых блоков {0}. Пустые слоты в этом списке - это нормально,",
            "Verdict: damaged blocks: {0}. Empty slots in this list are normal,"),
        ["report.verdictBad2"] = (
            "но повреждённый блок с персонажем игра покажет как \"Save data is corrupt\".",
            "but a damaged block with a character shows up in the game as \"Save data is corrupt\"."),
        ["dlg.andMore"] = (
            "... и ещё {0}",
            "... and {0} more"),
        ["dlg.deleteMany"] = (
            "Удалить {0}",
            "Delete {0}"),
        ["status.deletedOne"] = (
            "Снимок удалён",
            "Snapshot deleted"),
        ["status.deletedMany"] = (
            "Удалено файлов: {0}",
            "Files deleted: {0}"),
        ["status.deletedPartly"] = (
            "Удалено {0}, не удалось удалить {1}: {2}",
            "Deleted {0}, could not delete {1}: {2}"),
        ["status.autoSnap"] = (
            "Автоснимок: {0}",
            "Autosnapshot: {0}"),
        ["status.autoSnapRotated"] = (
            "Автоснимок: {0} (удалено старых: {1})",
            "Autosnapshot: {0} (old ones deleted: {1})"),
        ["foot.siteTip"] = (
            "Открыть krut.top в браузере",
            "Open krut.top in the browser"),
        ["foot.mailTip"] = (
            "Написать: admin@krut.top",
            "Write to admin@krut.top"),

        // ─── Журнал ─────────────────────────────────────────────────────
        ["log.toggle"] = ("Журнал операций", "Activity log"),
        ["log.section"] = ("ЖУРНАЛ ОПЕРАЦИЙ", "ACTIVITY LOG"),
        ["log.export"] = ("Выгрузить в файл", "Export to file"),
        ["log.hide"] = ("Скрыть журнал", "Hide the log"),
    };

    /// <summary>Все ключи - для проверки полноты перевода тестом.</summary>
    public static IReadOnlyCollection<string> Keys => Table.Keys;

    /// <summary>Обе строки ключа - для проверки перевода тестом.</summary>
    public static (string Ru, string En) Pair(string key) => Table[key];
}
