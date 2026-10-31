"""Evidence-based active selection. Original archive is never mutated."""
import datetime
import gzip
import hashlib
import json
import re
from pathlib import Path
from urllib.parse import unquote, urljoin, urlparse

from bs4 import BeautifulSoup

ROOT = Path(__file__).parent
AS_OF = datetime.date(2026, 10, 31)  # versioned policy; no time-dependent rebuilds
SUSPECT = re.compile(r'\b(event|anniversary|spotlight|festival|halloween|christmas)\b', re.I)
DATE_RANGE = re.compile(r'\((\d{4}/\d{1,2}/\d{1,2})\)\s*~\s*\((\d{4}/\d{1,2}/\d{1,2})\)')


def quest_key(url):
    return 'irowiki-classic:' + unquote(urlparse(url).path.split('/classic/', 1)[-1])


def snapshots(folder=ROOT/'sources/wiki'):
    path = folder/'manifest.json'
    if not path.exists():
        return {}
    data = json.loads(path.read_text())
    for value in data.values():
        raw = gzip.decompress((folder/value['path']).read_bytes())
        if hashlib.sha256(raw).hexdigest() != value['sha256']:
            raise ValueError('Source snapshot hash mismatch: ' + value['path'])
        value['html'] = raw
    return data


def source_membership(data):
    temporary, archived, current = {}, {}, {}
    for page, target in [('Category:Temporary_Event_Quests', temporary), ('Quests', archived)]:
        entry = data.get(page)
        if not entry:
            continue
        soup = BeautifulSoup(entry['html'], 'html.parser')
        if page.startswith('Category:'):
            area = soup.select_one('#mw-pages')
            if area:
                for a in area.select('.mw-category a[href]'):
                    target[quest_key(urljoin(entry['url'], a['href']))] = entry
        else:
            area = soup.select_one('.mw-parser-output')
            in_archive = False
            for node in area.descendants:
                if getattr(node, 'name', None) in ('h2', 'h3') and 'iRO Event Quests' in node.get_text():
                    in_archive = True
                if getattr(node, 'name', None) == 'a' and node.get('href','').startswith('/classic/'):
                    (archived if in_archive else current)[quest_key(urljoin(entry['url'],node['href']))] = entry
    return temporary, archived, current


def select_pages(source, data=None):
    data = snapshots() if data is None else data
    temporary, archived, current = source_membership(data)
    result = {}
    for pid, title, url, sha in source.execute('SELECT id,title,url,content_sha FROM wiki_pages ORDER BY id'):
        key = quest_key(url)
        state, reason, proof = 'included', 'catalog_candidate_availability_unknown', None
        dates = DATE_RANGE.search(title)
        ended = dates and datetime.datetime.strptime(dates[2], '%Y/%m/%d').date() < AS_OF
        edition = re.search(r'\b(20\d{2})\b', title)
        if key in archived and ended:
            state, reason, proof = 'excluded_archived', 'source_event_archive_and_explicit_ended_interval', archived[key]
        elif key in temporary and edition and int(edition[1]) < AS_OF.year:
            state, reason, proof = 'excluded_archived', 'source_temporary_event_category_and_historical_edition', temporary[key]
        elif key in current:
            reason, proof = 'source_current_quest_index_not_server_verification', current[key]
        elif key in temporary or SUSPECT.search(title):
            state, reason = 'quarantined', 'archival_status_requires_review_not_title_based_deletion'
            proof = temporary.get(key) or archived.get(key)
        result[pid] = {'quest_id':key, 'title':title, 'source_url':url, 'source_content_sha':sha,
            'state':state, 'reason':reason, 'as_of':AS_OF.isoformat(),
            'evidence_url':proof['url'] if proof else url,
            'evidence_sha256':proof['sha256'] if proof else sha,
            'evidence_text':title + (' | Temporary Event Quests' if key in temporary else
                ' | iRO Event Quests & Guides' if key in archived else
                ' | Current Quests' if key in current else '')}
    return result


def active_page_ids(source):
    return {pid for pid, r in select_pages(source).items() if r['state']=='included'}


def filter_catalog(catalog, source):
    """Filter the legacy research projection too, so no old usable export leaks events.

    Unrelated script rows remain explicitly tagged provenance, not active quests.
    The caller has committed its insert transaction before this function.
    """
    keep={r['quest_id'] for r in select_pages(source).values() if r['state']=='included'}
    catalog.commit()
    catalog.execute('PRAGMA foreign_keys=OFF')
    with catalog:
        catalog.execute('CREATE TEMP TABLE active_quest_keys(quest_key TEXT PRIMARY KEY)')
        catalog.executemany('INSERT INTO active_quest_keys VALUES(?)',((k,) for k in sorted(keep)))
        tables=[r[0] for r in catalog.execute("SELECT name FROM sqlite_master WHERE type='table'")]
        for table in tables:
            cols={r[1] for r in catalog.execute('PRAGMA table_info('+table+')')}
            for column in ('quest_key','prerequisite_key','dependency_key'):
                if column in cols:
                    catalog.execute(f'DELETE FROM {table} WHERE {column} NOT IN (SELECT quest_key FROM active_quest_keys)')
        catalog.execute('DROP TABLE active_quest_keys')
        catalog.execute("INSERT OR REPLACE INTO metadata VALUES('selection_policy','semantic active selection; archived and uncertain events excluded')")
        catalog.execute("INSERT OR REPLACE INTO metadata VALUES('script_tables_scope','provenance archive only; unrelated source code is not an active quest')")
    catalog.execute('PRAGMA foreign_keys=ON')
    violations=catalog.execute('PRAGMA foreign_key_check').fetchall()
    if violations:raise ValueError('Filtered legacy catalog has orphan relations: '+str(violations[:5]))


def filter_worklist(worklist, source):
    """Recompute the old worklist metrics from retained records, not archived totals."""
    from collections import Counter
    keep={r['quest_id'] for r in select_pages(source).values() if r['state']=='included'}
    rows=[r for r in worklist['quests'] if r['quest_key'] in keep]
    categories=Counter(r['source_category_candidate'] for r in rows)
    worklist['quests']=rows
    worklist['version']=3
    worklist['selection_policy']='evidence-based active selection; source archive is separate'
    worklist['metrics'].update(total_source_pages=len(rows),categories=dict(categories),
        core_research_candidates=sum(r['curation_action']=='prioritize_core_research' for r in rows),
        seasonal_archive_candidates=categories['historical_or_seasonal'],
        categorized_from_content=sum(r['category_method'] in ('explicit_guide_step','corroborated_guide_steps','wiki_requirements','dated_title') for r in rows),
        still_unsorted=categories['unsorted_quest_candidate'])
    return worklist
