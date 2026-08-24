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

Download counters are **cumulative** - GitHub counts from the moment a release is published
and never resets them. Downloads for a given day are the difference between two consecutive
snapshots. Referrers and paths are not daily slices but the 14-day totals GitHub shows in
Insights, so comparing them day to day tells you little; week to week it does.

Downloads of the automatic "Source code" archives are not counted by GitHub at all.
