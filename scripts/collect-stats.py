#!/usr/bin/env python3
"""
Сохраняет статистику репозитория в CSV рядом с кодом.

Зачем: GitHub держит трафик - просмотры, клоны, источники переходов - только
14 дней и старше не отдаёт никому, даже владельцу. Всё, что не сохранено
вовремя, исчезает навсегда. Счётчик скачиваний релизов, наоборот, живёт вечно,
но он кумулятивный: чтобы узнать, сколько скачали за конкретный день, нужны
ежедневные снимки.

Запуск: GITHUB_TOKEN=$(gh auth token) REPO=owner/name python3 scripts/collect-stats.py

Токену нужен доступ на запись в репозиторий: раздел traffic закрыт для всех,
кроме тех, у кого есть push. Скрипт ничего не требует, кроме стандартной
библиотеки, и запускается одинаково локально и в GitHub Actions.
"""

import csv
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone

REPO = os.environ.get("REPO", "valiDol42/erdtree-keeper")
TOKEN = os.environ.get("GITHUB_TOKEN", "")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
STATS = os.path.join(ROOT, "stats")
TODAY = datetime.now(timezone.utc).strftime("%Y-%m-%d")


def api(path):
    request = urllib.request.Request(f"https://api.github.com/repos/{REPO}/{path}")
    request.add_header("Accept", "application/vnd.github+json")
    request.add_header("X-GitHub-Api-Version", "2022-11-28")
    if TOKEN:
        request.add_header("Authorization", f"Bearer {TOKEN}")

    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # 403 на traffic означает токен без права записи - это стоит сказать
        # прямо, иначе выглядит как пустая статистика.
        print(f"  {path}: HTTP {error.code} {error.reason}", file=sys.stderr)
        raise


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
    while True:
        releases = api(f"releases?per_page=100&page={page}")
        if not releases:
            break
        for release in releases:
            for asset in release.get("assets", []):
                rows.append([
                    TODAY,
                    release.get("tag_name", ""),
                    asset.get("name", ""),
                    asset.get("download_count", 0),
                ])
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
    if not TOKEN:
        print("GITHUB_TOKEN не задан - раздел traffic закрыт без него", file=sys.stderr)

    collect_traffic()
    collect_downloads()
    collect_popular()


if __name__ == "__main__":
    main()
