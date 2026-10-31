"""Capture explicitly selected public wiki evidence, with robots policy and rate limiting.

Offline builds never fetch. Original HTML bytes are retained separately from parsed data.
"""
import argparse
import gzip
import hashlib
import json
import time
import urllib.parse
import urllib.robotparser
from pathlib import Path

import requests
from bs4 import BeautifulSoup

ROOT = Path(__file__).parent
UA = 'RoAura-Quest-Research/2.0 (public quest database source verification)'
DEFAULT_PAGES = ['Category:Temporary_Event_Quests', 'Quests', 'Juice_Quest',
    'Factory_Quest', 'Hugel_Memory_Quest', 'Gemstone_Exchanging',
    'Dropped_Knife_Quest', 'Prontera_Culverts_Entrance_Quest',
    'WoE_2_Guild_Dungeon_Event', 'Nifflheim_Spotlight', 'Glast_Heim_Spotlight',
    'Lighthalzen_Spotlight', 'Ragnarok_Tactics_Event', 'IRO_10th_Anniversary_Event']


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def capture(pages, folder=ROOT/'sources/wiki'):
    folder.mkdir(parents=True, exist_ok=True)
    manifest_path = folder/'manifest.json'
    manifest = json.loads(manifest_path.read_text()) if manifest_path.exists() else {}
    session = requests.Session()
    session.headers['User-Agent'] = UA
    robots = session.get('https://irowiki.org/robots.txt', timeout=30)
    robots.raise_for_status()
    policy = urllib.robotparser.RobotFileParser()
    policy.parse(robots.text.splitlines())
    for page in pages:
        url = 'https://irowiki.org/classic/' + page
        if not policy.can_fetch(UA, url):
            raise RuntimeError('robots.txt disallows ' + url)
        response = session.get(url, timeout=45)
        response.raise_for_status()
        raw = response.content
        soup = BeautifulSoup(raw, 'html.parser')
        root = soup.select_one('.mw-parser-output')
        if root is None or not soup.select_one('#firstHeading'):
            raise RuntimeError('Missing wiki article: ' + url)
        sha = digest(raw)
        filename = sha + '.html.gz'
        (folder/filename).write_bytes(gzip.compress(raw, mtime=0))
        categories = [a.get_text(' ', strip=True) for a in soup.select('#mw-normal-catlinks li a')]
        manifest[page] = {'url': url, 'sha256': sha, 'path': filename,
            'title': soup.select_one('#firstHeading').get_text(' ', strip=True),
            'categories': categories,
            'license': soup.select_one('#footer-info-copyright').get_text(' ', strip=True) if soup.select_one('#footer-info-copyright') else None,
            'captured_utc': time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime()),
            'page_links': sorted(set(urllib.parse.urljoin(url, a['href']) for a in root.select('a[href]')))}
        # Save after each successful page; failed later requests do not destroy evidence.
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
        print(page, categories, len(raw), flush=True)
        time.sleep(max(1, policy.crawl_delay(UA) or 0))
    session.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('pages', nargs='*')
    args = parser.parse_args()
    capture(args.pages or DEFAULT_PAGES)
