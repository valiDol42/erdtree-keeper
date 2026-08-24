# Статистика репозитория

Данные собирает [scripts/collect-stats.py](../scripts/collect-stats.py), запуск - раз в
сутки через [workflow «Статистика»](../.github/workflows/stats.yml).

Смысл затеи в одной строке: **GitHub хранит трафик только 14 дней**. Просмотры, клоны и
источники переходов старше двух недель не отдаются никому, включая владельца репозитория.
Что не сохранено вовремя - потеряно навсегда.

## Файлы

| Файл | Что внутри | Период |
| --- | --- | --- |
| `traffic.csv` | Просмотры и клоны по дням, всего и уникальных | По суткам, копится с первого запуска |
| `downloads.csv` | Счётчики скачивания файлов релизов | Снимок на дату |
| `referrers.csv` | Откуда приходили | Снимок на дату, сумма за 14 дней |
| `paths.csv` | Какие страницы репозитория открывали | Снимок на дату, сумма за 14 дней |
| `steam.csv` | Руководства Steam: посетители и избранное | Снимок на дату |

## Как читать

**`traffic.csv`** - обычный ряд по дням, ничего пересчитывать не нужно. `views` - все
открытия страниц, `unique_views` - сколько разных людей за этот день. Разрыв между ними
означает, что один и тот же человек ходил по репозиторию много раз.

**`downloads.csv`** - счётчики **кумулятивные**: GitHub считает скачивания с момента
выпуска релиза и никогда не сбрасывает. Скачивания за конкретный день - это разница между
снимками двух соседних дней:

```
2026-08-24,v1.3.1,ErdtreeKeeper-v1.3.1-win-x64.zip,2
2026-08-25,v1.3.1,ErdtreeKeeper-v1.3.1-win-x64.zip,9    <- за сутки 7
```

**`referrers.csv` и `paths.csv`** - не срез за сутки, а то, что GitHub показывает в
Insights: сумма за две недели на момент снимка. Сравнивать их между соседними днями
бессмысленно, зато видно, как менялась картина от недели к неделе.

**`steam.csv`** - уникальные посетители и число добавлений в избранное по каждому
руководству. Как и скачивания, это накопленные значения, а не срез за сутки: прирост за
день - разница между снимками.

Список руководств лежит в [`steam-guides.ids`](steam-guides.ids), по номеру на строку.
Номер виден в адресе руководства. Чтобы добавить новое, допишите строку - код править не
нужно.

Через Workshop API эти цифры не достать: `ISteamRemoteStorage` отвечает на такие номера
«файл не найден», руководства ему не принадлежат. Поэтому значения берутся с самой
страницы руководства, оттуда, где их показывает Steam. Разметка чужая: если Steam её
поменяет, сбор не сломается, а напишет предупреждение - и вот тогда разбор придётся
поправить. Строка с пустыми цифрами не записывается: дырка в данных честнее ложного нуля.

К адресу добавлен `&l=english`, и это не косметика. Без указания языка Steam на серверах
GitHub Actions молча перенаправлял запрос на общую страницу мастерской - в логе это
выглядело как «разметка поменялась», хотя руководство было на месте. С локальной машины
той же проблемы не было.

## Что нужно, чтобы собирался трафик

Скачивания собираются сами и никаких прав не требуют. С трафиком иначе:
**встроенный токен GitHub Actions получает на нём 403** при любых `permissions` - GitHub
отдаёт traffic только по personal access token. Без него ежедневный запуск будет собирать
одни скачивания и писать предупреждение.

Как включить, один раз:

1. [Создать fine-grained токен](https://github.com/settings/personal-access-tokens/new).
2. Repository access → Only select repositories → `erdtree-keeper`.
3. Repository permissions → **Administration** → Read-only. Больше ничего не нужно: ни
   доступа к коду, ни к секретам.
4. Скопировать токен.
5. В репозитории: Settings → Secrets and variables → Actions → New repository secret,
   имя `STATS_TOKEN`, значение - токен.

У fine-grained токенов есть срок действия. Когда он истечёт, сбор не сломается - в журнале
запуска появится предупреждение, а трафик начнёт пропускаться. Стоит завести напоминание.

Собрать вручную, не дожидаясь ночного запуска, можно и локально - права берутся из
авторизации `gh`:

```bash
GITHUB_TOKEN=$(gh auth token) REPO=valiDol42/erdtree-keeper python3 scripts/collect-stats.py
```

## Чего здесь нет

Скачивания автоматических архивов «Source code (zip)» и «(tar.gz)» GitHub не считает
вообще - этих цифр нет ни у кого.

Данные начинаются с 10 августа 2026 года: это те 14 дней, которые ещё лежали в окне GitHub
на момент первого запуска. Всё, что было раньше, потеряно - репозиторий тогда был закрытым,
и трафика всё равно не было.

---

# Repository statistics

Collected by [scripts/collect-stats.py](../scripts/collect-stats.py), run once a day by the
["Статистика" workflow](../.github/workflows/stats.yml).

The point, in one line: **GitHub keeps traffic data for 14 days only.** Views, clones and
referrers older than two weeks are not available to anyone, the repository owner included.
Whatever is not saved in time is gone for good.

| File | What is inside | Period |
| --- | --- | --- |
| `traffic.csv` | Views and clones per day, total and unique | Daily, accumulating from the first run |
| `downloads.csv` | Download counters of release assets | Snapshot per date |
| `referrers.csv` | Where visitors came from | Snapshot per date, 14-day total |
| `paths.csv` | Which repository pages were opened | Snapshot per date, 14-day total |
| `steam.csv` | Steam guides: unique visitors and favorites | Snapshot per date |

Download counters are **cumulative** - GitHub counts from the moment a release is published
and never resets them. Downloads for a given day are the difference between two consecutive
snapshots. Referrers and paths are not daily slices but the 14-day totals GitHub shows in
Insights, so comparing them day to day tells you little; week to week it does.

Downloads of the automatic "Source code" archives are not counted by GitHub at all.

**Steam guides** are listed in [`steam-guides.ids`](steam-guides.ids), one number per line -
add a line to track another one. The Workshop API refuses these ids ("file not found":
guides do not belong to it), so the numbers are read from the guide page itself. That markup
belongs to Steam, so a change there produces a warning rather than a failure.

**Traffic needs a token.** Download counters are public, but the traffic API returns 403 to
the built-in Actions token no matter the `permissions` - GitHub only serves it to a personal
access token. Create a fine-grained token limited to this repository with **Administration
(read)** and store it as the `STATS_TOKEN` repository secret. Without it the daily run
collects downloads only and says so in the log.
