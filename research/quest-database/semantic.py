"""Offline normalized ROQuestie database. Partial enrichment is explicitly measurable.

No emulator code is executed. No application/runtime module is imported or changed.
"""
import argparse
from collections import Counter
from contextlib import ExitStack, closing
import gzip
import hashlib
import json
import re
import sqlite3
from pathlib import Path
from urllib.parse import unquote, urlparse

from bs4 import BeautifulSoup
from selection import ROOT, snapshots, select_pages, quest_key

VERSION = 'semantic-5.2.0'
SCHEMA = ROOT/'migrations/001_semantic.sql'
BRANCH_SCHEMA = ROOT/'migrations/002_step_branches.sql'
ADDITION_SCHEMA = ROOT/'migrations/003_source_additions.sql'
ROUTE_SCHEMA = ROOT/'migrations/004_quest_routes.sql'
PREREQUISITE_SCHEMA = ROOT/'migrations/005_prerequisite_groups.sql'
SCHEMA_VERSION = 5
FIELDS = ('short_ru','short_en','full_ru','full_en')
MAP_COORD = re.compile(r'\b([a-zA-Z][a-zA-Z0-9_]{2,24})\s*(?:\)\)?\s*:\s*|[, ]?\s*\(?\s*)(\d{1,3})\s*(?:,\s*|\s+)(\d{1,3})(?!\d)')
BARE_COORD = re.compile(r'(?<![\d,])\b(\d{1,3})\s*,\s*(\d{1,3})(?![\d,])')
PRONOUN = re.compile(r'\b(?:return to|talk (?:with|to)|give|bring)\s+(?:him|her|them|the same NPC)\b',re.I)
ACTION = re.compile(r'\b(talk|speak|return|give|bring|collect|kill|choose|select|buy|use|receive|go|walk|find|show|check)\b', re.I)


def sha(value):
    return hashlib.sha256(value if isinstance(value,bytes) else value.encode()).hexdigest()


def stable(kind,*parts):
    return kind + ':' + sha(json.dumps(parts,ensure_ascii=False,separators=(',',':')))[:24]


def dump(path,data):
    path.write_text(json.dumps(data,ensure_ascii=False,sort_keys=True,indent=2)+'\n',encoding='utf-8')


def coordinates(text, known):
    """Return literal spans, never guess a map from unqualified numbers or town names."""
    matches=[]
    for m in MAP_COORD.finditer(text):
        name,x,y=m[1],int(m[2]),int(m[3])
        # Capitalized town labels are not silently normalized to map codes.
        known_code = name in known
        explicit_code = '_' in name and bool(re.search(r'\b(at|in|to|inside|visit|enter)\s*\(?\s*$',text[max(0,m.start()-35):m.start()],re.I))
        if (known_code or explicit_code) and 0<=x<=400 and 0<=y<=400:
            matches.append({'map':name,'x':x,'y':y,'start':m.start(),'end':m.end(),
                            'snippet':m[0],'known':known_code})
    for m in BARE_COORD.finditer(text):
        if any(n['start']<=m.start() and m.end()<=n['end'] for n in matches):continue
        prefix=text[max(0,m.start()-35):m.start()]
        suffix=text[m.end():m.end()+3]
        parenthesized=prefix.rstrip().endswith('(') and suffix.lstrip().startswith(')')
        explicit=bool(re.search(r'(?:\b(?:at|coordinates?|coords?)|:)\s*$',prefix,re.I))
        if (parenthesized or explicit) and int(m[1])<=400 and int(m[2])<=400:
            matches.append({'map':None,'x':int(m[1]),'y':int(m[2]),'start':m.start(),
                            'end':m.end(),'snippet':m[0],'known':False})
    return sorted(matches,key=lambda x:x['start'])


def resolve_context(steps):
    """A pronoun may use only one explicit interlocutor in the immediately previous step.

    Candidates from emulator code never establish an antecedent. Any intervening
    multi-NPC/narrative step clears it. Curated steps supply literal NPC names.
    """
    previous=[]; prior_step=None; result={}
    for step in steps:
        text=step['instruction']; names=step.get('explicit_interlocutors',[])
        if PRONOUN.search(text):
            result[step['step_no']]={'npc':previous[0] if len(previous)==1 else None,
                                    'support_step':prior_step if len(previous)==1 else None}
        previous=list(dict.fromkeys(names));prior_step=step['step_no']
    return result


def item_operation(sentence,name):
    """Conservative clause classification for a known item; quantities are separate.

    This helper never uses an item ID, coordinate or preceding sentence as a count.
    Ambiguous sentences require curation instead of conversion to requirements.
    """
    if name not in sentence:return 'mention'
    before=sentence[:sentence.index(name)]
    verbs=list(re.finditer(r'\b(receive|reward(?:s|ed)?|gives? you|gives? him|gives? her|bring|collect|buy|use|consume|kill|requires?|needs?|show)\b',before,re.I))
    if not verbs:return 'mention'
    v=verbs[-1][0].lower()
    if v.startswith(('receive','reward','give you','gives you')):return 'reward'
    if v in ('requires','require','needs','need'):return 'bring'
    if v.startswith(('give him','gives him','give her','gives her')):return 'deliver'
    return v


class Writer:
    def __init__(self,db):
        self.db=db;self.qid=None;self.sid=None;self.src=None

    def add(self,table,**row):
        cols=','.join(row); placeholders=','.join('?' for _ in row)
        self.db.execute(f'INSERT OR IGNORE INTO {table}({cols}) VALUES({placeholders})',tuple(row.values()))

    def evidence(self,snippet,method='literal_guide',status='source_literal',note=None,source=None,step=None):
        src=source or self.src; sid=self.sid if step is None else step
        eid=stable('ev',src,self.qid,sid,snippet,method,status,note)
        self.add('evidence',evidence_id=eid,source_id=src,quest_id=self.qid,step_id=sid,
                 snippet=snippet,method=method,status=status,note=note)
        return eid

    def review(self,reason,detail,evidence,severity='warning'):
        self.add('review_queue',review_id=stable('review',self.qid,self.sid,reason,detail),
                 quest_id=self.qid,step_id=self.sid,reason=reason,detail=detail,
                 evidence_id=evidence,severity=severity)

    def entity(self,kind,name,evidence,variant='irowiki-classic',identity=None,game_id=None,id_source=None,map_code=None,known=False):
        key=stable(kind,variant,identity or ('mapcode:'+map_code if kind=='map' and map_code else 'id:'+str(game_id) if game_id is not None else name))
        old=self.db.execute('SELECT original_name FROM entities WHERE entity_id=?',(key,)).fetchone()
        self.add('entities',entity_id=key,entity_type=kind,original_name=name,variant_id=variant,evidence_id=evidence)
        if old and old[0]!=name:self.add('entity_aliases',entity_id=key,alias=name,evidence_id=evidence)
        self.add('entity_evidence',entity_id=key,evidence_id=evidence)
        if kind=='npc':self.add('npcs',entity_id=key,game_id=game_id,identity_scope=identity or self.qid)
        elif kind=='map':self.add('maps',entity_id=key,map_code=map_code,known_in_reference=int(known))
        elif kind=='item':self.add('items',entity_id=key,game_id=game_id,id_source=id_source)
        elif kind=='monster':self.add('monsters',entity_id=key,game_id=game_id,id_source=id_source)
        return key

    def location(self,point,ev,role='waypoint',map_id=None):
        if point.get('map') and not map_id:
            map_id=self.entity('map',point['map'],ev,map_code=point['map'],known=point.get('known',False))
        lid=stable('location',map_id or self.sid,point['x'],point['y'])
        self.add('locations',location_id=lid,map_id=map_id,x=point['x'],y=point['y'],evidence_id=ev)
        self.add('step_locations',step_id=self.sid,location_id=lid,role=role,evidence_id=ev,
                 location_order=point.get('order'),condition_text=point.get('condition'))
        if not map_id:self.review('map_unknown','Coordinate pair has no established map code; not navigable.',ev)
        elif not point.get('known',False):self.review('map_not_reference_known','Map spelling has not been matched to a pinned reference map.',ev)
        return lid


def load_curations(folder):
    result={}
    for file in sorted(folder.glob('*.json')):
        row=json.loads(file.read_text())
        if 'quest_id' not in row:continue
        if row['quest_id'] in result:raise ValueError('Duplicate curated quest '+row['quest_id'])
        result[row['quest_id']]=row
    return result


def extract_links(snapshot):
    result=[]
    if not snapshot:return result
    for a in BeautifulSoup(snapshot['html'],'html.parser').select('.mw-parser-output a[href]'):
        m=re.search(r'db\.irowiki\.org/classic/(item|monster|map)-info/([^/]+)/',a['href'])
        if m and a.get_text(' ',strip=True):
            result.append({'type':m[1], 'name':a.get_text(' ',strip=True),
                           'id':int(m[2]) if m[2].isdigit() else m[2], 'url':a['href']})
    return result


def guide_payload(page, steps):
    """Pinned semantic inputs, independent of crawl timestamps and SQL row IDs."""
    return json.dumps({'title':page['title'],'requirements':page['requirements'],
                       'rewards':page['rewards'],'steps':[s['instruction'] for s in steps]},ensure_ascii=False)


def validate_curation(curation, page, steps, snapshot):
    if curation['source_sha256'] != sha(guide_payload(page, steps)):
        raise ValueError('Curated quest metadata/source changed; re-review required: '+curation['quest_id'])
    if not snapshot or curation['snapshot_sha256'] != snapshot['sha256']:
        raise ValueError('Curated HTML snapshot changed; re-review required: '+curation['quest_id'])
    if set(curation['steps']) != {str(s['step_no']) for s in steps}:
        raise ValueError('Curated step coverage mismatch: '+curation['quest_id'])
    ids=[s['step_id'] for s in curation['steps'].values() if s.get('step_id')]
    if len(ids)!=len(set(ids)):raise ValueError('Duplicate curated step identity')
    refs=[e.get('ref',e['name']) for e in curation['entities']]
    if len(refs)!=len(set(refs)):raise ValueError('Duplicate curated entity reference')


def mention_entities(w, entities, surfaces, entry):
    """Same-spelling NPCs require an explicit per-step choice, never first-match wins."""
    result={}
    for name,refs in surfaces.items():
        choices={entities[ref] for ref in refs}
        selected=(entry or {}).get('mention_entities',{}).get(name)
        if selected is not None:
            if selected not in refs:raise ValueError('Invalid mention entity choice: '+name)
            result[name]=entities[selected]
        elif len(choices)==1:
            result[name]=next(iter(choices))
        elif entry and any(name in entry[field] for field in FIELDS):
            raise ValueError('Ambiguous curated entity mention: '+name)
    return result


def descriptions(w,step,curated,entities,ev,refs=None,branch_id=None):
    text=step['instruction']
    if curated:
        if curated['instruction_sha256']!=sha(text):
            raise ValueError('Curated instruction changed; re-review required: '+w.sid)
        values={field:curated[field] for field in FIELDS}
        if not all(isinstance(x,str) and x.strip() for x in values.values()):raise ValueError('Incomplete curated localization')
        # Exact digits (including coordinates and counts) must survive the full translation.
        # Short descriptions may omit incidental prose; reviewers retain critical instructions.
        for ru,en in [('full_ru','full_en'),('short_ru','short_en')]:
            if Counter(re.findall(r'\d+',values[ru]))!=Counter(re.findall(r'\d+',values[en])):
                raise ValueError('Cross-language numeric mismatch: '+w.sid)
        for field in ('full_ru','full_en'):
            for name in curated.get('protected_names',[]):
                if name not in values[field]:raise ValueError('Translated/omitted protected name: '+name)
        for name in entities:
            pattern=r'(?<!\w)'+re.escape(name)+r'(?!\w)'
            for ru,en in [('full_ru','full_en'),('short_ru','short_en')]:
                if bool(re.search(pattern,values[ru])) != bool(re.search(pattern,values[en])):
                    raise ValueError('Cross-language entity mismatch: '+name+' in '+w.sid)
        for dialogue in curated.get('dialogues',[]):
            for option in dialogue.get('options',[]):
                if any(option['text'] not in values[f] for f in ('full_ru','full_en')):
                    raise ValueError('Translated/omitted exact dialogue answer: '+option['text'])
        state='source_reviewed'; editor='Codex source review; not human/server verification'
    else:
        values={'short_ru':None,'short_en':None,'full_ru':None,'full_en':text}
        state='pending_translation_and_semantic_review';editor=None
        w.review('localization_missing','Missing short_ru, short_en and full_ru; English original is retained without claiming bilingual completion.',ev,'blocking')
    owner={'branch_id':branch_id} if branch_id else {'step_id':w.sid}
    w.add('branch_descriptions' if branch_id else 'step_descriptions',**owner,**values,generation_version=VERSION,review_state=state,
          source_instruction_sha256=sha(text),editor=editor)
    overrides={}
    for rule in (curated or {}).get('mention_overrides',[]):
        key=(rule['field'],rule['surface'],rule['occurrence'])
        if key in overrides or rule['entity'] not in (refs or {}):raise ValueError('Invalid mention override')
        target=refs[rule['entity']]
        names={r[0] for r in w.db.execute('SELECT original_name FROM entities WHERE entity_id=? UNION SELECT alias FROM entity_aliases WHERE entity_id=?',(target[0],target[0]))}
        if rule['surface'] not in names:raise ValueError('Mention override has unsupported surface')
        overrides[key]=target
    used_overrides=set()
    # Resolve spans at build time, store typed Unicode code-point offsets for consumers.
    for field,value in values.items():
        if not value:continue
        occupied=[]
        for name,(eid,kind,eev) in sorted(entities.items(),key=lambda x:(-len(x[0]),x[0])):
            for occurrence,m in enumerate(re.finditer(r'(?<!\w)'+re.escape(name)+r'(?!\w)',value),1):
                if any(m.start()<end and start<m.end() for start,end in occupied):continue
                occupied.append(m.span())
                key=(field,name,occurrence)
                target=overrides.get(key,(eid,kind,eev))
                if key in overrides:used_overrides.add(key)
                w.add('branch_mentions' if branch_id else 'description_mentions',mention_id=stable('mention',branch_id or w.sid,field,*m.span()),
                      **owner,field=field,start_offset=m.start(),end_offset=m.end(),
                      entity_id=target[0],entity_type=target[1],surface=m[0],evidence_id=target[2])
        if curated and field=='full_ru':
            for name in curated.get('protected_names',[]):
                if name not in value:raise ValueError('Translated/omitted protected name: '+name)
    if set(overrides)!=used_overrides:raise ValueError('Unused mention override; text changed')


def curated_facts(w,entry,entities,ev,source_sections=None,html_source=None,branch_id=None,evidence_step_id=None):
    scope=branch_id or w.sid or w.qid
    def evidence_for(f):
        section=f.get('source_section')
        if section:
            snippet=f.get('evidence')
            if section not in (source_sections or {}) or not snippet or snippet not in source_sections[section]:
                raise ValueError('Nonliteral curated section evidence')
            return w.evidence(snippet,method='curated_'+section,source=html_source if section=='html_text' else None)
        snippet=f.get('evidence') or entry['original_instruction']
        # Context facts cite the actual supporting step, never pretend words occur here.
        support=f.get('support_step')
        if support:
            row=w.db.execute('SELECT step_id,original_instruction FROM quest_steps WHERE quest_id=? AND step_order=?',(w.qid,support)).fetchone()
            if row is None or snippet not in row[1]:raise ValueError('Invalid context witness')
            supporting=w.evidence(snippet,step=row[0])
            e=w.evidence(entry['original_instruction'],method='curated_quest_context',status='context_inferred',note=f.get('condition'))
            w.add('evidence_context',evidence_id=e,supporting_id=supporting)
            return e
        if snippet not in entry['original_instruction']:raise ValueError('Nonliteral curated evidence '+snippet)
        return w.evidence(snippet,method='curated_literal',step=evidence_step_id)
    def entity(name):
        if name not in entities:raise ValueError('Unknown curated entity '+name)
        return entities[name][0]
    action_offset=w.db.execute('SELECT coalesce(max(action_order),0) FROM step_actions WHERE step_id=?',(w.sid,)).fetchone()[0]
    for i,f in enumerate(entry.get('actions',[]),1):
        w.add('step_actions',action_id=stable('action',scope,i),step_id=w.sid,action_order=action_offset+i,branch_id=branch_id,
              operation=f['operation'],entity_id=entity(f['entity']) if f.get('entity') else None,
              condition_text=f.get('condition'),evidence_id=evidence_for(f))
    for f in entry.get('npcs',[]):
        if isinstance(f,str):f={'entity':f,'role':'talk'}
        w.add('step_npcs',step_id=w.sid,npc_id=entity(f['entity']),role=f['role'],evidence_id=evidence_for(f))
    for i,f in enumerate(entry.get('items',[])):
        w.add('step_items',fact_id=stable('item_fact',scope,i),step_id=w.sid,item_id=entity(f['entity']),branch_id=branch_id,
              operation=f['operation'],quantity=f.get('quantity'),required=f.get('required'),
              condition_text=f.get('condition'),evidence_id=evidence_for(f))
    for i,f in enumerate(entry.get('monsters',[])):
        w.add('step_monsters',fact_id=stable('monster_fact',scope,i),step_id=w.sid,monster_id=entity(f['entity']),branch_id=branch_id,
              operation=f['operation'],quantity=f.get('quantity'),required=f.get('required'),
              condition_text=f.get('condition'),evidence_id=evidence_for(f))
    for i,f in enumerate(entry.get('requirements',[])):
        w.add('requirements',requirement_id=stable('requirement',scope,i),quest_id=w.qid,step_id=w.sid,branch_id=branch_id,
              kind=f['kind'],entity_id=entity(f['entity']) if f.get('entity') else None,amount=f.get('amount'),
              condition_text=f.get('condition'),evidence_id=evidence_for(f))
    for i,f in enumerate(entry.get('rewards',[])):
        rid=stable('reward',scope,i)
        w.add('rewards',reward_id=rid,quest_id=w.qid,kind=f['kind'],item_id=entity(f['entity']) if f.get('entity') else None,branch_id=branch_id,
              amount=f.get('amount'),condition_text=f.get('condition'),evidence_id=evidence_for(f))
        if w.sid:w.add('step_rewards',step_id=w.sid,reward_id=rid)
    for i,f in enumerate(entry.get('dialogues',[]),1):
        e=evidence_for(f);did=stable('dialogue',scope,i)
        w.add('dialogues',dialogue_id=did,npc_id=entity(f['npc']) if f.get('npc') else None,
              original_text=f.get('text'),evidence_id=e,branch_id=branch_id)
        w.add('step_dialogues',step_id=w.sid,dialogue_id=did,dialogue_order=i)
        for j,o in enumerate(f.get('options',[]),1):
            witness=w.db.execute('SELECT snippet FROM evidence WHERE evidence_id=?',(e,)).fetchone()[0]
            if o['text'] not in witness:raise ValueError('Invented dialogue text')
            w.add('dialogue_options',option_id=stable('option',did,j),dialogue_id=did,option_order=j,
                  exact_text=o['text'],correct=o.get('correct'),operation=o.get('operation','choose'),
                  condition_text=o.get('condition'),evidence_id=e)
    for f in entry.get('locations',[]):
        e=evidence_for(f)
        w.location(f,e,f['role'],entity(f['entity']) if f.get('entity') else None)
    reuse=entry.get('action_reuse')
    if reuse:
        target=w.db.execute('SELECT step_id,step_order,original_instruction FROM quest_steps WHERE quest_id=? AND step_order=?',(w.qid,reuse['source_step'])).fetchone()
        current=w.db.execute('SELECT step_order FROM quest_steps WHERE step_id=?',(w.sid,)).fetchone()[0]
        if target is None or target[1]>=current:raise ValueError('Action reuse requires an earlier step in the same quest')
        e=w.evidence(entry['original_instruction'],method='curated_action_reuse',status='context_inferred',note=reuse['condition'])
        supporting=w.evidence(target[2],step=target[0])
        w.add('evidence_context',evidence_id=e,supporting_id=supporting)
        w.add('step_edges',from_step=w.sid,to_step=target[0],kind='repeat_actions_from',condition_text=reuse['condition'],evidence_id=e)
    for f in entry.get('reviews',[]):w.review(f['reason'],f['detail'],ev,f.get('severity','warning'))


def contextual_branches(w,curation,entities,surfaces,source_sections,html_source):
    rows={r['step_order']:dict(r) for r in w.db.execute('SELECT * FROM quest_steps WHERE quest_id=?',(w.qid,))}
    branch_ids={}
    def fragment(origin,target,start,end,section,branch=None,partition=None):
        text=rows[origin]['original_instruction']
        if not 0<=start<end<=len(text):raise ValueError('Invalid source fragment range')
        ev=w.evidence(text[start:end],method='source_section_fragment',step=rows[origin]['step_id'])
        fid=stable('fragment',rows[origin]['step_id'],rows[target]['step_id'],start,end,branch)
        w.add('source_fragments',fragment_id=fid,origin_step_id=rows[origin]['step_id'],target_step_id=rows[target]['step_id'],
              branch_id=branch,section=section,role='branch' if branch else 'primary',start_offset=start,end_offset=end,
              original_text=text[start:end],evidence_id=ev,partition_key=partition)
        if partition:w.db.execute('UPDATE source_fragments SET partition_key=? WHERE fragment_id=?',(partition,fid))
        return ev
    for n,entry in curation['steps'].items():
        target=int(n);w.sid=rows[target]['step_id']
        for b in entry.get('branches',[]):
            key=(target,b['key'])
            if key in branch_ids:raise ValueError('Duplicate branch key')
            origin=b.get('source_step',target)
            if origin not in rows:raise ValueError('Unknown branch source step')
            original=rows[origin]['original_instruction'];snippet=b['original_instruction']
            start=b.get('source_start',original.find(snippet));end=start+len(snippet)
            if start<0 or original[start:end]!=snippet:raise ValueError('Branch fragment is not literal source')
            if not b.get('condition') or not b.get('variant_scope'):raise ValueError('Branch condition/scope required')
            ev=w.evidence(snippet,method='source_branch_instruction',step=rows[origin]['step_id'])
            bid=stable('branch',w.sid,b['key']);branch_ids[key]=bid
            w.add('step_branches',branch_id=bid,step_id=w.sid,branch_key=b['key'],choice_group=b['choice_group'],
                  variant_scope=b['variant_scope'],condition_text=b['condition'],evidence_id=ev)
            fragment(origin,target,start,end,b['section'],bid)
            selected=mention_entities(w,entities,surfaces,b)
            descriptions(w,{'instruction':snippet},b,selected,ev,entities,branch_id=bid)
            curated_facts(w,b,entities,ev,source_sections,html_source,branch_id=bid,evidence_step_id=rows[origin]['step_id'])
    for part in curation.get('source_partitions',[]):
        origin=part['source_step']
        if origin not in rows:raise ValueError('Unknown partition source step')
        for seg in part['segments']:
            target=seg['target_step']
            if target not in rows:raise ValueError('Unknown partition target step')
            key=(target,seg.get('branch_key'))
            if seg.get('branch_key') and key not in branch_ids:raise ValueError('Unknown partition branch')
            w.sid=rows[target]['step_id']
            fragment(origin,target,seg['start'],seg['end'],seg['section'],branch_ids.get(key),rows[origin]['step_id'])


def source_additions(w,entry,source_sections,html_source):
    additions=entry.get('source_additions',[])
    if additions and not entry['full_ru'].endswith(' '.join(x['text_ru'] for x in additions)):
        raise ValueError('Source additions missing from translated description')
    for i,a in enumerate(additions,1):
        text=a['text_en'];section=a['source_section']
        if section!='html_text' or not html_source or not text.strip() or text not in source_sections[section]:
            raise ValueError('Nonliteral source addition')
        if not a['text_ru'].strip():raise ValueError('Missing source addition translation')
        if Counter(re.findall(r'\d+',text))!=Counter(re.findall(r'\d+',a['text_ru'])):raise ValueError('Source addition numeric mismatch')
        ev=w.evidence(text,method='curated_description_addition',source=html_source)
        w.add('step_source_additions',addition_id=stable('addition',w.sid,i),step_id=w.sid,addition_order=i,
              text_ru=a['text_ru'],text_en=text,evidence_id=ev)


def quest_routes(w,curation,source_sections,html_source):
    rows={r['step_order']:dict(r) for r in w.db.execute('SELECT * FROM quest_steps WHERE quest_id=?',(w.qid,))}
    seen=set()
    for route in curation.get('routes',[]):
        key=route['key'];members=route['steps'];heading=route.get('source_heading')
        kind=route['kind'];group=route.get('choice_group')
        if not route.get('condition') or kind not in ('alternative','optional') or (kind=='alternative' and not group) or (kind=='optional' and group is not None):raise ValueError('Invalid quest route kind/condition')
        if key in seen or not members or len(set(members))!=len(members):raise ValueError('Invalid duplicate/empty quest route')
        seen.add(key)
        if members!=sorted(members) or any(n not in rows for n in members):raise ValueError('Invalid quest route step order')
        if heading:
            if not html_source or heading not in source_sections['html_text']:raise ValueError('Nonliteral route heading')
            if any(rows[n]['section']!=heading for n in members):raise ValueError('Route membership contradicts source heading')
            ev=w.evidence(heading,method='source_route_heading',source=html_source)
        else:
            n=route['source_step']
            if members!=[n]:raise ValueError('Optional route needs its own source step')
            ev=w.evidence(rows[n]['original_instruction'],method='source_optional_activity',step=rows[n]['step_id'])
        rid=stable('route',w.qid,key)
        w.add('quest_routes',route_id=rid,quest_id=w.qid,route_key=key,kind=route['kind'],
              choice_group=route.get('choice_group'),condition_text=route['condition'],evidence_id=ev)
        for i,n in enumerate(members,1):
            sid=rows[n]['step_id']
            witness=w.evidence(rows[n]['original_instruction'],method='source_route_membership',status='context_inferred',
                               step=sid,note=route['condition'])
            w.add('evidence_context',evidence_id=witness,supporting_id=ev)
            w.add('step_routes',step_id=sid,route_id=rid,route_order=i,evidence_id=witness)


def curated_relations(w,curation):
    for relation in curation.get('relations',[]):
        target=w.db.execute('SELECT original_name FROM quests WHERE quest_id=?',(relation['related_quest_id'],)).fetchone()
        if relation['kind']=='mandatory_prerequisite':
            original=w.db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(w.qid,)).fetchone()[0] or ''
            prefix='Quest Prerequisite(s): '
            if not target or prefix not in original or original.split(prefix,1)[1].strip()!=target[0]:
                raise ValueError('Header does not establish a single mandatory prerequisite')
            ev=w.evidence(prefix+target[0],method='curated_single_prerequisite')
            w.add('quest_relations',quest_id=w.qid,related_id=relation['related_quest_id'],kind=relation['kind'],evidence_id=ev)
            continue
        step=w.db.execute('SELECT step_id,original_instruction FROM quest_steps WHERE quest_id=? AND step_order=?',(w.qid,relation['source_step'])).fetchone()
        if relation['kind'] not in ('source_concurrency_constraint','extra_stage_required_for_prerequisite','stage_required_to_start','completion_required_at_step') or not target or not step:
            raise ValueError('Invalid curated quest relation')
        snippet=relation.get('evidence',step[1])
        if snippet not in step[1] or target[0] not in snippet:raise ValueError('Unproven curated quest relation')
        ev=w.evidence(snippet,method='curated_quest_relation',step=step[0])
        w.add('quest_relations',quest_id=w.qid,related_id=relation['related_quest_id'],kind=relation['kind'],evidence_id=ev)


PHASE_PREFIXES={'required_quest':'','required_started_quest':'Started ','required_completed_quest':'Finished '}


def prerequisite_clause_proof(db,owner,related,kind,clause):
    target=db.execute('SELECT original_name FROM quests WHERE quest_id=?',(related,)).fetchone()
    if kind not in PHASE_PREFIXES or not target or owner==related:raise ValueError('Invalid phase prerequisite')
    expected=PHASE_PREFIXES[kind]+target[0]
    if not re.fullmatch(re.escape(expected)+r'(?: \([^()]+\))?',clause):raise ValueError('Phase prerequisite contradicts source clause')
    return clause


def phase_prerequisites(w,curation):
    clauses=curation.get('prerequisite_clauses',[])
    if not clauses:return
    original=w.db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(w.qid,)).fetchone()[0] or ''
    expected=' , '.join(x['evidence'] for x in clauses)
    if 'Quest Prerequisite(s): ' not in original or original.split('Quest Prerequisite(s): ',1)[1]!=expected:
        raise ValueError('Phase prerequisite list contradicts source header')
    if len({x['related_quest_id'] for x in clauses})!=len(clauses):raise ValueError('Duplicate phase prerequisite')
    for clause in clauses:
        text=prerequisite_clause_proof(w.db,w.qid,clause['related_quest_id'],clause['kind'],clause['evidence'])
        ev=w.evidence(text,method='curated_prerequisite_clause')
        w.add('quest_relations',quest_id=w.qid,related_id=clause['related_quest_id'],kind=clause['kind'],evidence_id=ev)


def prerequisite_group_proof(db,owner,minimum,members):
    if not isinstance(minimum,int) or isinstance(minimum,bool) or not 0<minimum<=len(members) or len(set(members))!=len(members) or owner in members:
        raise ValueError('Invalid prerequisite threshold or members')
    names=[]
    for related in members:
        row=db.execute('SELECT original_name FROM quests WHERE quest_id=?',(related,)).fetchone()
        if row is None:raise ValueError('Unknown prerequisite group member')
        names.append(row[0])
    text=f"Quest Prerequisite(s): {minimum} of the following {len(members)} quests: "+' , '.join(names)
    original=db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(owner,)).fetchone()[0] or ''
    if 'Quest Prerequisite(s): ' not in original or original.split('Quest Prerequisite(s): ',1)[1]!=text.split('Quest Prerequisite(s): ',1)[1]:
        raise ValueError('Prerequisite group contradicts source header')
    return text


def prerequisite_groups(w,curation):
    seen=set()
    for group in curation.get('prerequisite_groups',[]):
        key=group['key'];members=group['members'];minimum=group['minimum_completed']
        if not key or key in seen:raise ValueError('Duplicate/empty prerequisite group key')
        seen.add(key)
        text=prerequisite_group_proof(w.db,w.qid,minimum,members)
        ev=w.evidence(text,method='curated_prerequisite_group')
        gid=stable('prerequisite_group',w.qid,key)
        w.add('quest_prerequisite_groups',group_id=gid,quest_id=w.qid,group_key=key,minimum_completed=minimum,evidence_id=ev)
        for n,related in enumerate(members,1):
            w.add('quest_prerequisite_members',group_id=gid,related_id=related,member_order=n,evidence_id=ev)


def migrate_semantic(db):
    """Upgrade a committed research export without rewriting existing content."""
    version=db.execute('PRAGMA user_version').fetchone()[0]
    if version in (1,2,3,4):
        if db.in_transaction:raise ValueError('Commit pending changes before semantic migration')
        migrations=([BRANCH_SCHEMA] if version==1 else [])+([ADDITION_SCHEMA] if version<=2 else [])+([ROUTE_SCHEMA] if version<=3 else [])+[PREREQUISITE_SCHEMA]
        try:db.executescript('BEGIN;\n'+'\n'.join(p.read_text() for p in migrations)+'\nCOMMIT;')
        except Exception:
            db.rollback()
            raise
    elif version!=SCHEMA_VERSION:raise ValueError('Unsupported semantic schema version '+str(version))


def validate(db):
    integrity=db.execute('PRAGMA integrity_check').fetchone()[0]
    foreign=db.execute('PRAGMA foreign_key_check').fetchall()
    errors=[]
    previous={}
    for row in db.execute('SELECT m.step_id,m.field,m.start_offset,m.end_offset,m.surface,d.short_ru,d.short_en,d.full_ru,d.full_en FROM description_mentions m JOIN step_descriptions d USING(step_id) ORDER BY m.step_id,m.field,m.start_offset'):
        sid,field,start,end,surface,*values=row
        value=values[FIELDS.index(field)]
        if value is None or value[start:end]!=surface:errors.append('invalid_mention_span')
        key=(sid,field)
        if start<previous.get(key,0):errors.append('overlapping_mentions')
        previous[key]=end
    for original,digest,description_digest in db.execute('SELECT original_instruction,instruction_sha256,source_instruction_sha256 FROM quest_steps JOIN step_descriptions USING(step_id)'):
        if sha(original)!=digest or digest!=description_digest:errors.append('source_instruction_hash')
    if db.execute("SELECT count(*) FROM evidence WHERE status='server_verified'").fetchone()[0]:errors.append('unsupported_server_claim')
    if db.execute('SELECT count(*) FROM step_images WHERE rights_status NOT IN (\'authorized\',\'licensed\')').fetchone()[0]:errors.append('unlicensed_images')
    for step,short_ru,short_en,ru,en in db.execute('SELECT step_id,short_ru,short_en,full_ru,full_en FROM step_descriptions WHERE full_ru IS NOT NULL'):
        if Counter(re.findall(r'\d+',ru))!=Counter(re.findall(r'\d+',en)):errors.append('numeric_locale_parity')
        if not all((short_ru,short_en,ru,en)):errors.append('partial_curated_description')
        elif Counter(re.findall(r'\d+',short_ru))!=Counter(re.findall(r'\d+',short_en)):errors.append('short_numeric_locale_parity')
    if db.execute('PRAGMA user_version').fetchone()[0]>=2:
        if db.execute('SELECT count(*) FROM step_branches b LEFT JOIN branch_descriptions d USING(branch_id) WHERE d.branch_id IS NULL').fetchone()[0]:errors.append('branch_description_missing')
        if db.execute('SELECT 1 FROM step_branches b LEFT JOIN source_fragments f USING(branch_id) WHERE f.fragment_id IS NULL LIMIT 1').fetchone():errors.append('branch_source_missing')
        if db.execute('''SELECT 1 FROM step_branches b JOIN evidence e USING(evidence_id)
          LEFT JOIN source_fragments f ON f.branch_id=b.branch_id AND f.original_text=e.snippet
          AND f.origin_step_id=e.step_id WHERE f.fragment_id IS NULL LIMIT 1''').fetchone():errors.append('branch_evidence_mismatch')
        if db.execute('''SELECT 1 FROM step_branches b JOIN evidence e USING(evidence_id)
          JOIN quest_steps s ON s.step_id=b.step_id WHERE e.quest_id<>s.quest_id LIMIT 1''').fetchone():errors.append('branch_evidence_wrong_quest')
        for row in db.execute('SELECT d.short_ru,d.short_en,d.full_ru,d.full_en,d.source_instruction_sha256,e.snippet FROM branch_descriptions d JOIN step_branches b USING(branch_id) JOIN evidence e USING(evidence_id)'):
            sr,se,fr,fe,digest,snippet=row
            if digest!=sha(snippet):errors.append('branch_source_hash')
            if Counter(re.findall(r'\d+',sr))!=Counter(re.findall(r'\d+',se)) or Counter(re.findall(r'\d+',fr))!=Counter(re.findall(r'\d+',fe)):errors.append('branch_numeric_locale_parity')
        previous={}
        for row in db.execute('SELECT m.branch_id,m.field,m.start_offset,m.end_offset,m.surface,d.short_ru,d.short_en,d.full_ru,d.full_en FROM branch_mentions m JOIN branch_descriptions d USING(branch_id) ORDER BY m.branch_id,m.field,m.start_offset'):
            bid,field,start,end,surface,*values=row;key=(bid,field)
            if values[FIELDS.index(field)][start:end]!=surface:errors.append('invalid_branch_mention_span')
            if start<previous.get(key,0):errors.append('overlapping_branch_mentions')
            previous[key]=end
        partitions={}
        fragments=db.execute('''SELECT f.*,s.original_instruction AS source_text,s.quest_id AS origin_quest,
          t.quest_id AS target_quest,b.step_id AS branch_step,e.step_id AS evidence_step,e.snippet AS evidence_text
          FROM source_fragments f JOIN quest_steps s ON s.step_id=f.origin_step_id
          JOIN quest_steps t ON t.step_id=f.target_step_id LEFT JOIN step_branches b USING(branch_id)
          JOIN evidence e ON e.evidence_id=f.evidence_id''')
        names=[d[0] for d in fragments.description]
        for row in fragments:
            f=dict(zip(names,row))
            if not 0<=f['start_offset']<f['end_offset']<=len(f['source_text']) or f['source_text'][f['start_offset']:f['end_offset']]!=f['original_text']:errors.append('source_fragment_mismatch')
            if f['origin_quest']!=f['target_quest'] or (f['branch_id'] and f['branch_step']!=f['target_step_id']):errors.append('fragment_target_mismatch')
            if f['evidence_step']!=f['origin_step_id'] or f['evidence_text']!=f['original_text']:errors.append('fragment_evidence_mismatch')
            if f['partition_key']:
                if f['partition_key']!=f['origin_step_id']:errors.append('source_partition_wrong_origin')
                partitions.setdefault(f['partition_key'],[]).append(f)
        for parts in partitions.values():
            position=0
            for f in sorted(parts,key=lambda x:x['start_offset']):
                if f['start_offset']!=position:errors.append('source_partition_gap_or_overlap')
                position=f['end_offset']
            if position!=len(parts[0]['source_text']):errors.append('source_partition_incomplete')
        for sid,text in db.execute('''SELECT s.step_id,d.full_en FROM quest_steps s JOIN step_descriptions d USING(step_id)
          WHERE d.full_en<>s.original_instruction'''):
            primary=[r[0].strip() for r in db.execute("SELECT original_text FROM source_fragments WHERE target_step_id=? AND role='primary' AND partition_key IS NOT NULL ORDER BY start_offset",(sid,))]
            additions=[]
            if db.execute('PRAGMA user_version').fetchone()[0]>=3:
                additions=[r[0] for r in db.execute('SELECT text_en FROM step_source_additions WHERE step_id=? ORDER BY addition_order',(sid,))]
            original=db.execute('SELECT original_instruction FROM quest_steps WHERE step_id=?',(sid,)).fetchone()[0]
            expected=' '.join((primary or [original])+additions)
            if (not primary and not additions) or expected!=text:errors.append('edited_description_without_partition')
        for table in ('step_actions','step_items','step_monsters','requirements'):
            if db.execute(f'SELECT 1 FROM {table} f JOIN step_branches b USING(branch_id) WHERE f.step_id IS NULL OR f.step_id<>b.step_id LIMIT 1').fetchone():errors.append('branch_fact_wrong_step')
        if db.execute('SELECT 1 FROM rewards r JOIN step_branches b USING(branch_id) LEFT JOIN step_rewards s ON s.reward_id=r.reward_id AND s.step_id=b.step_id WHERE s.reward_id IS NULL LIMIT 1').fetchone():errors.append('branch_reward_wrong_step')
        if db.execute('SELECT 1 FROM dialogues d JOIN step_branches b USING(branch_id) LEFT JOIN step_dialogues s ON s.dialogue_id=d.dialogue_id AND s.step_id=b.step_id WHERE s.dialogue_id IS NULL LIMIT 1').fetchone():errors.append('branch_dialogue_wrong_step')
    if db.execute('PRAGMA user_version').fetchone()[0]>=3:
        for row in db.execute('''SELECT a.text_ru,a.text_en,e.snippet,e.method,src.kind,d.full_ru,d.full_en,
          a.step_id,e.step_id,e.quest_id,s.quest_id
          FROM step_source_additions a JOIN evidence e USING(evidence_id) JOIN sources src USING(source_id)
          JOIN step_descriptions d ON d.step_id=a.step_id JOIN quest_steps s ON s.step_id=a.step_id'''):
            ru,en,snippet,method,kind,full_ru,full_en,sid,source_sid,source_quest,quest=row
            if en!=snippet or method!='curated_description_addition' or kind!='html_snapshot':errors.append('source_addition_evidence')
            if sid!=source_sid or quest!=source_quest:errors.append('source_addition_wrong_step')
            if ru not in (full_ru or '') or en not in (full_en or ''):errors.append('source_addition_missing_from_description')
            if Counter(re.findall(r'\d+',ru))!=Counter(re.findall(r'\d+',en)):errors.append('source_addition_numeric_parity')
    if db.execute('PRAGMA user_version').fetchone()[0]>=4:
        if db.execute('''SELECT 1 FROM step_routes m JOIN quest_routes r USING(route_id) JOIN quest_steps s USING(step_id)
          JOIN evidence e ON e.evidence_id=m.evidence_id
          WHERE s.quest_id IS NOT r.quest_id OR e.quest_id IS NOT r.quest_id OR e.step_id IS NOT m.step_id LIMIT 1''').fetchone():errors.append('route_membership_wrong_quest_or_step')
        if db.execute('''SELECT 1 FROM quest_routes r JOIN evidence e USING(evidence_id)
          LEFT JOIN step_routes m USING(route_id) WHERE e.quest_id IS NOT r.quest_id OR m.route_id IS NULL LIMIT 1''').fetchone():errors.append('route_evidence_or_members_missing')
        if db.execute('''SELECT route_id FROM step_routes GROUP BY route_id
          HAVING min(route_order)<>1 OR max(route_order)<>count(*) LIMIT 1''').fetchone():errors.append('route_order_gap')
    if db.execute('''SELECT 1 FROM step_edges edge JOIN quest_steps a ON a.step_id=edge.from_step
      JOIN quest_steps b ON b.step_id=edge.to_step JOIN evidence ev ON ev.evidence_id=edge.evidence_id
      WHERE edge.kind='repeat_actions_from' AND (a.quest_id IS NOT b.quest_id OR b.step_order>=a.step_order
      OR ev.step_id IS NOT edge.from_step OR ev.quest_id IS NOT a.quest_id OR ev.method<>'curated_action_reuse'
      OR ev.snippet<>a.original_instruction OR NOT EXISTS (SELECT 1 FROM evidence_context c
      JOIN evidence support ON support.evidence_id=c.supporting_id WHERE c.evidence_id=edge.evidence_id
      AND support.step_id=edge.to_step AND support.snippet=b.original_instruction)) LIMIT 1''').fetchone():errors.append('invalid_action_reuse_target')
    if db.execute('''SELECT 1 FROM quest_relations r JOIN evidence e USING(evidence_id) LEFT JOIN quest_steps s ON s.step_id=e.step_id
      JOIN quests target ON target.quest_id=r.related_id
      WHERE r.kind IN ('source_concurrency_constraint','extra_stage_required_for_prerequisite','stage_required_to_start','completion_required_at_step')
      AND (e.method<>'curated_quest_relation' OR e.quest_id IS NOT r.quest_id OR s.quest_id IS NOT r.quest_id
      OR instr(s.original_instruction,e.snippet)=0 OR instr(e.snippet,target.original_name)=0) LIMIT 1''').fetchone():errors.append('invalid_curated_quest_relation')
    for owner,kind,related,eid in db.execute("SELECT quest_id,kind,related_id,evidence_id FROM quest_relations WHERE kind='mandatory_prerequisite'"):
        proof=db.execute('SELECT quest_id,step_id,snippet,method FROM evidence WHERE evidence_id=?',(eid,)).fetchone()
        requirement=db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(owner,)).fetchone()[0] or ''
        name=db.execute('SELECT original_name FROM quests WHERE quest_id=?',(related,)).fetchone()[0]
        prefix='Quest Prerequisite(s): '
        if proof[0]!=owner or proof[1] is not None or proof[2]!=prefix+name or proof[3]!='curated_single_prerequisite' or prefix not in requirement or requirement.split(prefix,1)[1].strip()!=name:
            errors.append('invalid_mandatory_prerequisite')
    owners={r[0] for r in db.execute("SELECT quest_id FROM quest_relations WHERE kind IN ('required_quest','required_started_quest','required_completed_quest') UNION SELECT quest_id FROM evidence WHERE method='curated_prerequisite_clause'")}
    for owner in owners:
        clauses=[]
        for related,kind,eowner,sid,text,method in db.execute('''SELECT r.related_id,r.kind,e.quest_id,e.step_id,e.snippet,e.method
          FROM quest_relations r JOIN evidence e USING(evidence_id) WHERE r.quest_id=?
          AND r.kind IN ('required_quest','required_started_quest','required_completed_quest')''',(owner,)):
            try:prerequisite_clause_proof(db,owner,related,kind,text)
            except ValueError:errors.append('invalid_phase_prerequisite')
            if eowner!=owner or sid is not None or method!='curated_prerequisite_clause':errors.append('invalid_phase_prerequisite_evidence')
            clauses.append(text)
        original=db.execute('SELECT requirements_original FROM quests WHERE quest_id=?',(owner,)).fetchone()[0] or ''
        if 'Quest Prerequisite(s): ' not in original or Counter(original.split('Quest Prerequisite(s): ',1)[1].split(' , '))!=Counter(clauses):
            errors.append('incomplete_phase_prerequisite_list')
    if db.execute('PRAGMA user_version').fetchone()[0]>=5:
        for gid,owner,minimum,eid in db.execute('SELECT group_id,quest_id,minimum_completed,evidence_id FROM quest_prerequisite_groups'):
            members=db.execute('SELECT related_id,member_order,evidence_id FROM quest_prerequisite_members WHERE group_id=? ORDER BY member_order',(gid,)).fetchall()
            try:expected=prerequisite_group_proof(db,owner,minimum,[r[0] for r in members])
            except ValueError:errors.append('invalid_prerequisite_group');continue
            proof=db.execute('SELECT quest_id,step_id,snippet,method FROM evidence WHERE evidence_id=?',(eid,)).fetchone()
            if tuple(proof)!=(owner,None,expected,'curated_prerequisite_group') or [r[1] for r in members]!=list(range(1,len(members)+1)) or any(r[2]!=eid for r in members):
                errors.append('invalid_prerequisite_group_evidence')
    if integrity!='ok' or foreign or errors:raise ValueError(f'Invalid semantic DB: {integrity}, FK={foreign}, errors={errors}')
    return {'integrity_check':integrity,'foreign_key_violations':len(foreign),'invalid_mentions':len(errors)}


def build(source_path,output,curation_dir=ROOT/'curation',cache_dir=None):
    # Failed review gates must close SQLite before another build reuses the temp path.
    with ExitStack() as resources:
        return _build(source_path,output,curation_dir,cache_dir,resources)


def _build(source_path,output,curation_dir,cache_dir,resources):
    output=Path(output);output.mkdir(parents=True,exist_ok=True)
    source=resources.enter_context(closing(sqlite3.connect(f'file:{Path(source_path).resolve()}?mode=ro',uri=True)))
    source.row_factory=sqlite3.Row
    data=snapshots(); decisions=select_pages(source,data); curations=load_curations(curation_dir)
    active={pid:r for pid,r in decisions.items() if r['state']=='included'}
    missing=set(curations)-{r['quest_id'] for r in active.values()}
    if missing:raise ValueError('Curation references excluded/missing quest: '+str(missing))
    cache_dir=Path(cache_dir) if cache_dir else output/'.semantic-cache'
    cache_dir.mkdir(parents=True,exist_ok=True)
    destination=output/'quest_semantic.sqlite';temp=output/'quest_semantic.build.sqlite'
    if destination.exists():
        previous_db=sqlite3.connect(f'file:{destination.resolve()}?mode=ro',uri=True)
        try:
            if previous_db.execute("SELECT count(*) FROM step_descriptions WHERE review_state='human_reviewed'").fetchone()[0]:
                raise ValueError('Human-reviewed database rows must be exported to pinned curation before rebuilding; refusing overwrite.')
            if previous_db.execute('PRAGMA user_version').fetchone()[0]>=2 and previous_db.execute("SELECT count(*) FROM branch_descriptions WHERE review_state='human_reviewed'").fetchone()[0]:
                raise ValueError('Human-reviewed branch rows must be exported to pinned curation before rebuilding; refusing overwrite.')
            if previous_db.execute('PRAGMA user_version').fetchone()[0]>=3 and previous_db.execute("SELECT count(*) FROM step_source_additions WHERE review_state='human_reviewed'").fetchone()[0]:
                raise ValueError('Human-reviewed source additions must be exported to pinned curation before rebuilding; refusing overwrite.')
            if previous_db.execute('PRAGMA user_version').fetchone()[0]>=4 and previous_db.execute("SELECT count(*) FROM quest_routes WHERE review_state='human_reviewed'").fetchone()[0]:
                raise ValueError('Human-reviewed quest routes must be exported to pinned curation before rebuilding; refusing overwrite.')
            if previous_db.execute('PRAGMA user_version').fetchone()[0]>=5 and previous_db.execute("SELECT count(*) FROM quest_prerequisite_groups WHERE review_state='human_reviewed'").fetchone()[0]:
                raise ValueError('Human-reviewed prerequisite groups must be exported to pinned curation before rebuilding; refusing overwrite.')
        finally:
            previous_db.close()
    if temp.exists():temp.unlink()
    db=resources.enter_context(closing(sqlite3.connect(temp)));db.row_factory=sqlite3.Row;db.executescript(SCHEMA.read_text());migrate_semantic(db);w=Writer(db)
    w.add('variants',variant_id='irowiki-classic',game_variant='Classic',assurance='source_scope_not_server_verified')
    known={r[0] for r in source.execute('SELECT DISTINCT map FROM script_npcs WHERE map IS NOT NULL')}
    code_hash=sha(Path(__file__).read_bytes()+SCHEMA.read_bytes()+BRANCH_SCHEMA.read_bytes()+ADDITION_SCHEMA.read_bytes()+ROUTE_SCHEMA.read_bytes()+PREREQUISITE_SCHEMA.read_bytes())
    cache_hits=0;plans=[]
    for pid,selection in active.items():
        page=dict(source.execute('SELECT * FROM wiki_pages WHERE id=?',(pid,)).fetchone())
        qid=selection['quest_id'];curation=curations.get(qid)
        steps=[dict(r) for r in source.execute('''SELECT s.*,t.source_step,t.kind FROM wiki_steps s
            LEFT JOIN wiki_step_sources t USING(page_id,step_no) WHERE s.page_id=? ORDER BY s.step_no''',(pid,))]
        snap=data.get(unquote(urlparse(page['url']).path.split('/classic/',1)[-1]))
        if curation:validate_curation(curation,page,steps,snap)
        input_hash=sha(json.dumps([page,steps,curation,selection,snap['sha256'] if snap else None,sorted(known),code_hash],sort_keys=True,ensure_ascii=False))
        cache_path=cache_dir/(input_hash+'.json')
        if cache_path.exists():
            plan=json.loads(cache_path.read_text());cache_hits+=1
        else:
            plan={'page':page,'steps':steps,'curation':curation,'selection':selection,
                  'locations':{str(s['step_no']):coordinates(s['instruction'],known) for s in steps}}
            dump(cache_path,plan)
        plans.append((plan,snap,input_hash))
    for plan,snap,input_hash in plans:
        page=plan['page'];steps=plan['steps'];curation=plan['curation'];sel=plan['selection']
        w.qid=sel['quest_id'];w.sid=None
        source_text=guide_payload(page,steps)
        html_text=BeautifulSoup(snap['html'],'html.parser').select_one('.mw-parser-output').get_text(' ',strip=True) if snap else ''
        source_sections={'requirements':page['requirements'] or '', 'rewards':page['rewards'] or '', 'html_text':html_text}
        w.src=stable('source',page['url'],sha(source_text))
        w.add('sources',source_id=w.src,url=page['url'],revision=sha(source_text),kind='archived_extracted_guide',original_text=source_text,
              archive_path='output/quest_research.sqlite',license='iRO Wiki: GFDL 1.3 or later; original source notices apply')
        category=source.execute('SELECT category FROM wiki_content_categories WHERE page_id=?',(page['id'],)).fetchone()
        w.add('quests',quest_id=w.qid,original_name=page['title'],category=category[0] if category else 'unclassified',
              variant_id='irowiki-classic',source_id=w.src,requirements_original=page['requirements'],rewards_original=page['rewards'],
              base_level=page['base_level'],job_level=page['job_level'],review_state='source_reviewed_partial' if curation else 'needs_context_review',input_hash=input_hash)
        page_ev=w.evidence(source_text,method='archived_guide_metadata')
        if snap:
            html_source=stable('source',snap['url'],snap['sha256'])
            w.add('sources',source_id=html_source,url=snap['url'],revision=snap['sha256'],kind='html_snapshot',
                  archive_path='sources/wiki/'+snap['path'],license=snap['license'])
        else:
            html_source=None
            w.review('original_html_missing','Previous collector retained extracted prose and an HTML hash, not original HTML bytes.',page_ev)
        w.review('server_availability_unknown','Classic wiki and emulator matches do not verify any individual server.',page_ev,'info')
        if not curation:w.review('quest_context_unreviewed','Requirements, rewards and every branch still need whole-quest semantic review.',page_ev,'blocking')
        if not steps:w.review('walkthrough_missing','No preserved walkthrough steps.',page_ev,'blocking')
        for kind,column in [('requirements_original','requirements'),('rewards_original','rewards')]:
            if page[column] and not curation:w.review(kind+'_unstructured','Preserved raw metadata has not been converted to asserted gameplay facts.',w.evidence(page[column]))
        for kind,column in [('base_level','base_level'),('job_level','job_level')]:
            if page[column] is not None:
                w.add('requirements',requirement_id=stable('requirement',w.qid,kind),quest_id=w.qid,kind=kind,amount=str(page[column]),evidence_id=page_ev)
        seen=Counter();previous=None
        for step in steps:
            h=sha(step['instruction']);seen[h]+=1
            sid=w.qid+'/step/'+h[:20]+'/'+str(seen[h])
            curated=curation['steps'].get(str(step['step_no'])) if curation else None
            if curated and curated.get('step_id'):
                sid=curated['step_id']
                if not sid.startswith(w.qid+'/step/'):raise ValueError('Curated step identity belongs to another quest')
            step['step_id']=sid
            if curation and not curated:raise ValueError('Curated quest has unreviewed step '+sid)
            w.add('quest_steps',step_id=sid,quest_id=w.qid,step_order=step['step_no'],source_step=step['source_step'],source_kind=step['kind'],
                  section=step['section'],original_instruction=step['instruction'],instruction_sha256=h,
                  primary_goal=curated['goal'] if curated else 'unresolved',review_state='source_reviewed' if curated else 'pending')
        entities={};surfaces={}
        if curation:
            # All names must have literal support somewhere in this complete quest.
            for e in curation['entities']:
                if e['name'] not in source_text and e['name'] not in html_text:raise ValueError('Entity absent from quest '+e['name'])
                ref=e.get('ref',e['name'])
                ev=w.evidence(e['name'],method='curated_entity_in_quest',source=html_source if e['name'] not in source_text else None)
                eid=w.entity(e['type'],e['name'],ev,identity=e.get('identity') or (w.qid+':'+ref if e['type']=='npc' else None),
                    game_id=e.get('game_id'),id_source=e.get('id_source'),map_code=e.get('map_code'),known=e.get('map_code') in known)
                entities[ref]=(eid,e['type'],ev)
                surfaces.setdefault(e['name'],[]).append(ref)
                for alias in e.get('aliases',[]):
                    if alias not in source_text:raise ValueError('Unproven alias')
                    w.add('entity_aliases',entity_id=eid,alias=alias,evidence_id=ev);entities[alias]=(eid,e['type'],ev)
                    surfaces.setdefault(alias,[]).append(alias)
                if e.get('game_id') is not None:
                    matches=[a for a in extract_links(snap) if a['name'] in [e['name']]+e.get('aliases',[]) and a['id']==e['game_id'] and a['type']==e['type']]
                    if not matches or not html_source:raise ValueError('Unverified game ID for '+e['name'])
                    iev=w.evidence(matches[0]['url'],method='wiki_database_id_link',source=html_source)
                    w.add('entity_evidence',entity_id=eid,evidence_id=iev)
            if curation.get('quest_facts'):
                curated_facts(w,curation['quest_facts'],entities,page_ev,source_sections,html_source)
        for step in steps:
            w.sid=step['step_id'];ev=w.evidence(step['instruction'])
            curated=curation['steps'].get(str(step['step_no'])) if curation else None
            step_entities=mention_entities(w,entities,surfaces,curated)
            if previous:
                w.add('step_edges',from_step=previous,to_step=w.sid,kind='source_order_only',evidence_id=ev)
            previous=w.sid
            if curated:
                if curated['original_instruction']!=step['instruction']:raise ValueError('Curation original mismatch')
                curated_facts(w,curated,entities,ev,source_sections,html_source)
                source_additions(w,curated,source_sections,html_source)
            else:
                w.review('step_semantics_unreviewed','Literal hints only. Items, NPC identities, dialogue, conditions and goals remain unreviewed.',ev,'blocking')
                if PRONOUN.search(step['instruction']):w.review('unresolved_pronoun','No uniquely reviewed NPC antecedent; context link withheld.',ev)
            for p in plan['locations'][str(step['step_no'])]:
                pe=w.evidence(p['snippet'],method='literal_coordinate_span')
                if p['map']:
                    mid=w.entity('map',p['map'],pe,map_code=p['map'],known=p['known'])
                    step_entities[p['map']]=(mid,'map',pe)
                # Curated location roles replace generic duplicate roles for identical point.
                if not curated:w.location(p,pe)
            descriptions(w,step,curated,step_entities,ev,entities)
        if curation:
            contextual_branches(w,curation,entities,surfaces,source_sections,html_source)
            w.sid=None
            quest_routes(w,curation,source_sections,html_source)
            for issue in curation.get('reviews',[]):w.review(issue['reason'],issue['detail'],page_ev,issue.get('severity','warning'))
    w.qid=None;w.sid=None
    for plan,_,_ in plans:
        if not plan['curation']:continue
        w.qid=plan['selection']['quest_id'];w.src=db.execute('SELECT source_id FROM quests WHERE quest_id=?',(w.qid,)).fetchone()[0]
        curated_relations(w,plan['curation'])
        phase_prerequisites(w,plan['curation'])
        prerequisite_groups(w,plan['curation'])
    w.qid=None
    for row in source.execute('SELECT page_id,prerequisite_page_id,matched_title FROM wiki_quest_prerequisite_links'):
        if row[0] not in active:continue
        w.qid=active[row[0]]['quest_id'];w.src=db.execute('SELECT source_id FROM quests WHERE quest_id=?',(w.qid,)).fetchone()[0]
        ev=w.evidence(row[2],method='literal_quest_prerequisite_reference')
        if row[1] in active:w.add('quest_relations',quest_id=w.qid,related_id=active[row[1]]['quest_id'],kind='prerequisite_boolean_unspecified',evidence_id=ev)
        else:w.review('prerequisite_not_in_active_catalog','Referenced prerequisite excluded/quarantined; no orphan relation created.',ev,'blocking')
    data_version=sha(json.dumps([x[2] for x in plans]+list(decisions.values()),sort_keys=True))
    w.add('metadata',key='schema_version',value=str(SCHEMA_VERSION));w.add('metadata',key='data_version',value=data_version)
    w.add('metadata',key='generator_version',value=VERSION);w.add('metadata',key='completion',value='partial; inspect semantic_report.json')
    checks=validate(db)
    tables=[r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
    counts={t:db.execute('SELECT count(*) FROM '+t).fetchone()[0] for t in tables}
    localized=db.execute('SELECT count(*) FROM ready_steps').fetchone()[0]
    status_counts=dict(db.execute('SELECT status,count(*) FROM evidence GROUP BY status'))
    report={'schema_version':SCHEMA_VERSION,'data_version':data_version,'generator_version':VERSION,'complete':False,
        'source_pages':len(decisions),'selection_counts':dict(Counter(r['state'] for r in decisions.values())),
        'curated_quests':len(curations),'pending_quests':counts['quests']-len(curations),
        'fully_localized_branches':counts['branch_descriptions'],
        'source_additions':counts['step_source_additions'],
        'quest_routes':counts['quest_routes'],
        'action_reuse_links':db.execute("SELECT count(*) FROM step_edges WHERE kind='repeat_actions_from'").fetchone()[0],
        'prerequisite_groups':counts['quest_prerequisite_groups'],
        'phase_prerequisites':db.execute("SELECT count(*) FROM quest_relations WHERE kind IN ('required_quest','required_started_quest','required_completed_quest')").fetchone()[0],
        'counts':counts,'fully_localized_steps':localized,'not_fully_localized_steps':counts['quest_steps']-localized,
        'localized_fields':{f:db.execute(f'SELECT count(*) FROM step_descriptions WHERE {f} IS NOT NULL').fetchone()[0] for f in FIELDS},
        'evidence_status_counts':status_counts,'checks':checks,'server_verified':0,
        'limitations':['Only curated quests have whole-quest semantic analysis and full bilingual text.',
            'All other retained rows have original English prose and explicit blocking review entries.',
            'Source order is not an executable branch graph; server availability is unknown.',
            'Uncertain event status is quarantined, not asserted retired.',
            'Legacy source extraction may omit tables/images and contains nonquest reference pages.']}
    queue=[dict(r) for r in db.execute('SELECT * FROM review_queue ORDER BY quest_id,step_id,reason,review_id')]
    examples=[]
    for qid in sorted(curations):
        record={'quest_id':qid,'steps':[]}
        for row in db.execute('SELECT * FROM localized_steps WHERE quest_id=? ORDER BY step_order',(qid,)):
            r=dict(zip([d[0] for d in db.execute('SELECT * FROM localized_steps LIMIT 0').description],row))
            sid=r['step_id']
            for table in ('step_npcs','step_locations','step_items','step_monsters','step_dialogues','step_rewards','step_actions','description_mentions'):
                r[table]=[dict(x) for x in db.execute('SELECT * FROM '+table+' WHERE step_id=?',(sid,))]
            r['branches']=[dict(x) for x in db.execute('SELECT b.*,d.short_ru,d.short_en,d.full_ru,d.full_en FROM step_branches b JOIN branch_descriptions d USING(branch_id) WHERE b.step_id=? ORDER BY b.branch_key',(sid,))]
            for b in r['branches']:
                b['mentions']=[dict(x) for x in db.execute('SELECT * FROM branch_mentions WHERE branch_id=? ORDER BY field,start_offset',(b['branch_id'],))]
            r['source_fragments']=[dict(x) for x in db.execute('SELECT * FROM source_fragments WHERE target_step_id=? ORDER BY origin_step_id,start_offset',(sid,))]
            r['source_additions']=[dict(x) for x in db.execute('SELECT * FROM step_source_additions WHERE step_id=? ORDER BY addition_order',(sid,))]
            r['routes']=[dict(x) for x in db.execute('SELECT m.*,r.route_key,r.kind,r.choice_group,r.condition_text FROM step_routes m JOIN quest_routes r USING(route_id) WHERE m.step_id=? ORDER BY r.route_key',(sid,))]
            r['action_reuse']=[dict(x) for x in db.execute("SELECT * FROM step_edges WHERE from_step=? AND kind='repeat_actions_from'",(sid,))]
            record['steps'].append(r)
        record['entities']=[dict(x) for x in db.execute('SELECT DISTINCT e.* FROM entities e JOIN entity_evidence l USING(entity_id) JOIN evidence v ON v.evidence_id=l.evidence_id WHERE v.quest_id=?',(qid,))]
        record['rewards']=[dict(x) for x in db.execute('SELECT * FROM rewards WHERE quest_id=?',(qid,))]
        record['quest_relations']=[dict(x) for x in db.execute('SELECT * FROM quest_relations WHERE quest_id=? ORDER BY kind,related_id',(qid,))]
        record['prerequisite_groups']=[dict(x) for x in db.execute('SELECT * FROM quest_prerequisite_groups WHERE quest_id=? ORDER BY group_key',(qid,))]
        for group in record['prerequisite_groups']:
            group['members']=[dict(x) for x in db.execute('SELECT * FROM quest_prerequisite_members WHERE group_id=? ORDER BY member_order',(group['group_id'],))]
        record['dialogue_options']=[dict(x) for x in db.execute('SELECT o.* FROM dialogue_options o JOIN step_dialogues d USING(dialogue_id) JOIN quest_steps s USING(step_id) WHERE s.quest_id=?',(qid,))]
        examples.append(record)
    db.commit();db.execute('VACUUM');db.close();source.close()
    temp.replace(destination)
    (output/'quest_semantic.sqlite.gz').write_bytes(gzip.compress(destination.read_bytes(),mtime=0))
    dump(output/'semantic_report.json',report)
    dump(output/'semantic_selection.json',{'as_of':'2026-10-31','records':list(decisions.values())})
    dump(output/'semantic_review_queue.json',{'complete':False,'records':queue})
    dump(output/'semantic_examples.json',examples)
    dump(output/'active_quests.json',{'data_version':data_version,'complete':False,'quests':list(active.values())})
    print(json.dumps({'quests':counts['quests'],'steps':counts['quest_steps'],'localized':localized,'selection':report['selection_counts'],
                      'integrity':checks,'processed':len(plans)-cache_hits,'cache_hits':cache_hits}),flush=True)
    return report


if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,default=ROOT/'output/quest_research.sqlite')
    p.add_argument('--out',type=Path,default=ROOT/'output');p.add_argument('--cache',type=Path)
    args=p.parse_args();build(args.source,args.out,cache_dir=args.cache)
