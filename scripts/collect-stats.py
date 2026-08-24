#!/usr/bin/env python3
"""
Сохраняет статистику репозитория в CSV рядом с кодом.

Зачем: GitHub держит трафик - просмотры, клоны, источники переходов - только
14 дней и старше не отдаёт никому, даже владельцу. Всё, что не сохранено
вовремя, исчезает навсегда. Счётчик скачиваний релизов, наоборот, живёт вечно,
но он кумулятивный: чтобы узнать, сколько скачали за конкретный день, нужны
ежедневные снимки.

Запуск: GITHUB_TOKEN=$(gh auth token) REPO=owner/name python3 scripts/collect-stats.py

Про токен. Счётчики скачивания открыты всем и токена не требуют вовсе. А вот
traffic закрыт: встроенный GITHUB_TOKEN из Actions получает на нём 403 при
любых permissions - нужен personal access token с правом Administration (read).
Без него скрипт соберёт скачивания и честно скажет, что трафик пропущен.

Скрипт ничего не требует, кроме стандартной библиотеки, и запускается одинаково
локально и в GitHub Actions.
"""

import csv
import json
import os
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone

REPO = os.environ.get("REPO", "valiDol42/erdtree-keeper")
TOKEN = os.environ.get("GITHUB_TOKEN", "")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATS = os.path.join(ROOT, "stats")
TODAY = datetime.now(timezone.utc).strftime("%Y-%m-%d")


def api(path, attempts=3):
    """
    Запрос к API с повтором при временных сбоях.

    Сбор идёт раз в сутки без присмотра, и случайный 502 или 504 со стороны
    GitHub не должен стоить дня статистики: трафик за пропущенный день уже не
    вернуть. Отказы по правам (401, 403, 404) повторять бессмысленно - они
    возвращаются сразу.
    """
    request = urllib.request.Request(f"https://api.github.com/repos/{REPO}/{path}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("X-GitHub-Api-Version", "2022-11-28")
    # GitHub требует User-Agent и без него отвечает не пойми чем: анонимный
    # запрос со стандартным заголовком urllib возвращал 504.
    request.add_header("User-Agent", "erdtree-keeper-stats")
    if TOKEN:
        request.add_header("Authorization", f"Bearer {TOKEN}")

    for attempt in range(1, attempts + 1):
        try:
            with urllib.request.urlopen(request, timeout=30) as response:
                return json.load(response)
        except urllib.error.HTTPError as error:
            if error.code in (401, 403, 404) or attempt == attempts:
                print(f"  {path}: HTTP {error.code} {error.reason}", file=sys.stderr)
                raise
            print(f"  {path}: HTTP {error.code}, попытка {attempt} из {attempts}", file=sys.stderr)
        except urllib.error.URLError as error:
            if attempt == attempts:
                print(f"  {path}: сеть недоступна ({error.reason})", file=sys.stderr)
                raise
            print(f"  {path}: сеть недоступна, попытка {attempt} из {attempts}", file=sys.stderr)

        time.sleep(attempt * 5)


def merge(name, header, key_columns, rows):
    """
    Дописывает строки в CSV, заменяя совпадающие по ключу.

    Замена, а не добавление: за сегодняшний день цифры в течение суток растут,
    и каждый следующий запуск должен уточнять уже записанное, а не плодить
    дубли.
    """
    path = os.path.join(STATS, name)
    known = {}

    if os.path.exists(path):
        with open(path, newline="", encoding="utf-8") as file:
            for row in csv.reader(file):
                if row and row != header:
                    known[tuple(row[:key_columns])] = row

    added = 0
    for row in rows:
        row = [str(value) for value in row]
        key = tuple(row[:key_columns])
        if key not in known:
            added += 1
        known[key] = row

    os.makedirs(STATS, exist_ok=True)
    with open(path, "w", newline="", encoding="utf-8") as file:
        writer = csv.writer(file, lineterminator="\n")
        writer.writerow(header)
        for key in sorted(known):
            writer.writerow(known[key])

    print(f"  {name}: строк {len(known)}, из них новых {added}")


def skipped(what, error):
    """
    Нет прав на traffic - это не поломка, а отсутствующий секрет. Скачивания
    собираются и без него, поэтому сбор продолжается, но молчать об этом
    нельзя: иначе пустая статистика выглядит как отсутствие трафика.
    """
    if error.code in (401, 403, 404):
        why = ("Нужен секрет STATS_TOKEN - personal access token с правом "
               "Administration (read); встроенному токену Actions traffic закрыт.")
    else:
        why = "Похоже на временный сбой GitHub - следующий запуск заберёт эти дни."

    print(f"::warning::{what} пропущен: HTTP {error.code}. {why}", file=sys.stderr)


def collect_traffic():
    """Просмотры и клоны по дням - то, что пропадает через две недели."""
    views = {v["timestamp"][:10]: v for v in api("traffic/views?per=day").get("views", [])}
    clones = {c["timestamp"][:10]: c for c in api("traffic/clones?per=day").get("clones", [])}

    rows = []
    for day in sorted(set(views) | set(clones)):
        view = views.get(day, {})
        clone = clones.get(day, {})
        rows.append([
            day,
            view.get("count", 0), view.get("uniques", 0),
            clone.get("count", 0), clone.get("uniques", 0),
        ])

    merge("traffic.csv",
          ["date", "views", "unique_views", "clones", "unique_clones"],
          1, rows)


def collect_downloads():
    """
    Снимок счётчиков скачивания. Значения кумулятивные: разница между двумя
    днями и есть скачивания за день.
    """
    rows = []
    page = 1
    per_page = 100
    while True:
        releases = api(f"releases?per_page={per_page}&page={page}")
        for release in releases:
            for asset in release.get("assets", []):
                rows.append([
                    TODAY,
                    release.get("tag_name", ""),
                    asset.get("name", ""),
                    asset.get("download_count", 0),
                ])

        # Неполная страница - последняя. Спрашивать следующую не только лишнее,
        # но и ненадёжно: на пустую страницу GitHub отвечал 504.
        if len(releases) < per_page:
            break
        page += 1

    merge("downloads.csv", ["date", "tag", "asset", "downloads"], 3, rows)


def collect_popular():
    """
    Источники переходов и популярные страницы. GitHub отдаёт их суммой за
    последние 14 дней, поэтому в файле это снимок на дату, а не срез за сутки.
    """
    referrers = [
        [TODAY, item.get("referrer", ""), item.get("count", 0), item.get("uniques", 0)]
        for item in api("traffic/popular/referrers")
    ]
    merge("referrers.csv", ["snapshot_date", "referrer", "count", "uniques"], 2, referrers)

    paths = [
        [TODAY, item.get("path", ""), item.get("count", 0), item.get("uniques", 0)]
        for item in api("traffic/popular/paths")
    ]
    merge("paths.csv", ["snapshot_date", "path", "count", "uniques"], 2, paths)


def main():
    print(f"Репозиторий: {REPO}, дата снимка: {TODAY}")

    collected = 0

    for name, collect in (("Трафик", collect_traffic), ("Источники переходов", collect_popular)):
        try:
            collect()
            collected += 1
        except urllib.error.HTTPError as error:
            skipped(name, error)
        except urllib.error.URLError as error:
            print(f"::warning::{name} пропущен: сеть недоступна ({error.reason})", file=sys.stderr)

    # Скачивания открыты всем и токена не требуют - если не собрались даже они,
    # сломалось что-то настоящее.
    collect_downloads()
    collected += 1

    if collected == 1:
        print("Собраны только скачивания. Трафик за эти дни будет потерян "
              "безвозвратно: GitHub хранит его 14 дней.", file=sys.stderr)


if __name__ == "__main__":
    main()
