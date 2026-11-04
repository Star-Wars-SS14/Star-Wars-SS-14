#!/usr/bin/env python3
"""Split Content.IntegrationTests into CI shards and print a `dotnet test --filter` expression for one of them.

    python Tools/_SW/ci_shard.py --index 0 --count 4      # filter for shard 0 of 4
    python Tools/_SW/ci_shard.py --count 4 --list         # table of shards, weights and classes
    python Tools/_SW/ci_shard.py --count 4 --check        # every test class lands in exactly one shard

Test classes are found by scanning the C# sources: a class is a test class when it is not abstract and it, or a base
class, has [Test], [TestCase] or [TestCaseSource] methods. Partial classes are merged by full name.

Weight of a class is an estimate of its run time in seconds: seconds_per_test for each [Test]/[TestCase], and
seconds_per_case_source for each [TestCaseSource], unless Tools/_SW/ci_shard_weights.json gives the class an
explicit weight. Classes listed as heavy there are spread over different shards first, then the rest are packed
greedily (largest first, into the lightest shard).

The last shard is a catch-all: instead of listing its classes it excludes the classes of every other shard. A test
class the scanner misses, or one added later, therefore still runs, in the last shard.
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TESTS_DIR = os.path.join(ROOT, 'Content.IntegrationTests')
WEIGHTS_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'ci_shard_weights.json')
ASSEMBLY_PREFIX = 'Content.IntegrationTests'

CLASS_RE = re.compile(
    r'\b((?:(?:public|internal|private|protected|sealed|abstract|static|partial|unsafe|file)\s+)*)'
    r'(class|struct|record|interface)\s+(\w+)\s*(<[^>{;]*>)?\s*(?:\([^)]*\))?\s*(?::\s*([^{;]+))?')
NAMESPACE_RE = re.compile(r'\bnamespace\s+([\w.]+)\s*([;{])')
TEST_ATTR_RE = re.compile(r'[\[,]\s*(?:NUnit\.Framework\.)?(Test|TestCase|TestCaseSource)\s*(?=[\](,])')


def strip_code(text):
    """Blank out comments, strings and char literals, keeping offsets, so braces and keywords inside them are ignored."""
    out = list(text)
    i, n = 0, len(text)

    def blank(a, b):
        for k in range(a, b):
            if out[k] != '\n':
                out[k] = ' '

    while i < n:
        c = text[i]
        if text.startswith('//', i):
            j = text.find('\n', i)
            j = n if j < 0 else j
            blank(i, j)
            i = j
        elif text.startswith('/*', i):
            j = text.find('*/', i + 2)
            j = n if j < 0 else j + 2
            blank(i, j)
            i = j
        elif text.startswith('"""', i):
            j = text.find('"""', i + 3)
            j = n if j < 0 else j + 3
            while j < n and text[j] == '"':
                j += 1
            blank(i, j)
            i = j
        elif c == '"' or (c in '@$' and i + 1 < n and text[i + 1] in '"@$'):
            k = i
            verbatim = False
            while k < n and text[k] in '@$':
                verbatim |= text[k] == '@'
                k += 1
            if k >= n or text[k] != '"':
                i += 1
                continue
            if text.startswith('"""', k):  # interpolated raw string, $"""...""" or $$"""..."""
                j = text.find('"""', k + 3)
                j = n if j < 0 else j + 3
                while j < n and text[j] == '"':
                    j += 1
                blank(i, j)
                i = j
                continue
            j = k + 1
            while j < n:
                if verbatim:
                    if text[j] == '"':
                        if j + 1 < n and text[j + 1] == '"':
                            j += 2
                            continue
                        break
                else:
                    if text[j] == '\\':
                        j += 2
                        continue
                    if text[j] in '"\n':
                        break
                j += 1
            blank(i, min(j + 1, n))
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and text[j] not in "'\n":
                j += 2 if text[j] == '\\' else 1
            blank(i, min(j + 1, n))
            i = j + 1
        else:
            i += 1
    return ''.join(out)


class ClassInfo:
    def __init__(self, fqn, name):
        self.fqn = fqn
        self.name = name
        self.abstract = False
        self.bases = set()
        self.tests = 0
        self.case_sources = 0
        self.files = set()


def scan(tests_dir=TESTS_DIR):
    classes = {}
    for dirpath, dirnames, filenames in os.walk(tests_dir):
        dirnames[:] = [d for d in dirnames if d not in ('bin', 'obj')]
        for filename in filenames:
            if filename.endswith('.cs'):
                scan_file(os.path.join(dirpath, filename), classes)
    return classes


def scan_file(path, classes):
    with open(path, encoding='utf-8-sig', errors='replace') as f:
        code = strip_code(f.read())

    events = []
    for m in NAMESPACE_RE.finditer(code):
        events.append((m.start(), 'ns', m))
    for m in CLASS_RE.finditer(code):
        events.append((m.start(), 'class', m))
    for m in TEST_ATTR_RE.finditer(code):
        events.append((m.start(), 'test', m))
    for i, c in enumerate(code):
        if c in '{}':
            events.append((i, c, None))
    events.sort(key=lambda e: e[0])

    file_ns = ''
    stack = []  # entries: ('ns', name) / ('class', ClassInfo or None) / ('block', None)
    pending = None
    for pos, kind, m in events:
        if kind == 'ns':
            if m.group(2) == ';':
                file_ns = m.group(1)
            else:
                pending = ('ns', m.group(1))
        elif kind == 'class':
            if m.group(2) != 'class':
                pending = ('class', None)
                continue
            outer = [s[1] for s in stack if s[0] == 'class']
            if outer and outer[-1] is None:
                pending = ('class', None)
                continue
            ns = '.'.join([file_ns] * bool(file_ns) + [s[1] for s in stack if s[0] == 'ns'])
            name = m.group(3)
            if outer:
                fqn = outer[-1].fqn + '+' + name
            else:
                fqn = (ns + '.' if ns else '') + name
            if m.group(4):
                fqn += '`' + str(m.group(4).count(',') + 1)
            info = classes.setdefault(fqn, ClassInfo(fqn, name))
            info.files.add(os.path.relpath(path, ROOT).replace('\\', '/'))
            if 'abstract' in m.group(1).split() or 'static' in m.group(1).split():
                info.abstract = True
            if m.group(5):
                for base in m.group(5).split(','):
                    base = re.sub(r'<.*', '', base.strip()).split('.')[-1].strip()
                    if base:
                        info.bases.add(base)
            pending = ('class', info)
        elif kind == 'test':
            owner = next((s[1] for s in reversed(stack) if s[0] == 'class'), None)
            if owner is not None:
                if m.group(1) == 'TestCaseSource':
                    owner.case_sources += 1
                else:
                    owner.tests += 1
        elif kind == '{':
            stack.append(pending if pending else ('block', None))
            pending = None
        elif kind == '}':
            if stack:
                stack.pop()


def test_classes(classes):
    """Non-abstract classes that own tests or inherit them. Returns {fqn: (tests, case_sources)}."""
    by_name = {}
    for info in classes.values():
        by_name.setdefault(info.name, []).append(info)

    def counts(info, seen):
        tests, sources = info.tests, info.case_sources
        for base in info.bases:
            for b in by_name.get(base, []):
                if b.fqn not in seen:
                    seen.add(b.fqn)
                    t, s = counts(b, seen)
                    tests += t
                    sources += s
        return tests, sources

    result = {}
    for info in classes.values():
        if info.abstract:
            continue
        t, s = counts(info, {info.fqn})
        if t or s:
            result[info.fqn] = (t, s)
    return result


def load_weights(path=WEIGHTS_FILE):
    if not os.path.exists(path):
        return {}
    with open(path, encoding='utf-8') as f:
        return json.load(f)


def lookup(table, fqn):
    short = fqn.rsplit('.', 1)[-1]
    if fqn in table:
        return table[fqn]
    return table.get(short)


def weigh(found, config):
    per_test = config.get('seconds_per_test', 2)
    per_source = config.get('seconds_per_case_source', 10)
    overrides = config.get('classes', {})
    weights = {}
    for fqn, (tests, sources) in found.items():
        w = lookup(overrides, fqn)
        weights[fqn] = float(w) if w is not None else tests * per_test + sources * per_source
    heavy = set()
    for name in config.get('heavy', []):
        matched = [f for f in found if f == name or f.rsplit('.', 1)[-1] == name]
        heavy.update(matched)
    unknown = [n for n in list(overrides) + config.get('heavy', [])
               if not any(f == n or f.rsplit('.', 1)[-1] == n for f in found)]
    return weights, heavy, unknown


def pack(weights, heavy, count):
    bins = [{'load': 0.0, 'classes': [], 'heavy': 0} for _ in range(count)]
    order = sorted(heavy, key=lambda f: (-weights[f], f))
    for fqn in order:
        b = min(bins, key=lambda b: (b['heavy'], b['load']))
        b['classes'].append(fqn)
        b['load'] += weights[fqn]
        b['heavy'] += 1
    rest = sorted((f for f in weights if f not in heavy), key=lambda f: (-weights[f], f))
    for fqn in rest:
        b = min(bins, key=lambda b: b['load'])
        b['classes'].append(fqn)
        b['load'] += weights[fqn]
    # The catch-all shard excludes every other shard's classes, so give that role to the shard with the most classes:
    # its filter is then the shortest possible negation.
    catch_all = max(range(count), key=lambda i: (len(bins[i]['classes']), -i))
    bins.append(bins.pop(catch_all))
    for b in bins:
        b['classes'].sort()
    return bins


def token(fqn):
    # Trailing dot: `Foo.` must not also match `FooBar.Method`. Nested classes appear as `Outer+Inner` in the name.
    return fqn + '.'


def shard_filter(bins, index):
    count = len(bins)
    if index < count - 1:
        return '|'.join(f'FullyQualifiedName~{token(f)}' for f in bins[index]['classes'])
    terms = [f'FullyQualifiedName~{ASSEMBLY_PREFIX}.']
    for b in bins[:-1]:
        terms.extend(f'FullyQualifiedName!~{token(f)}' for f in b['classes'])
    return '&'.join(terms)


def matches(filter_text, name):
    """Evaluate the restricted filters this script prints (only '|' or only '&', operators ~ and !~)."""
    def term(t):
        if '!~' in t:
            return t.split('!~', 1)[1] not in name
        return t.split('~', 1)[1] in name
    if '&' in filter_text:
        return all(term(t) for t in filter_text.split('&'))
    return any(term(t) for t in filter_text.split('|'))


def build(count, config=None):
    config = load_weights() if config is None else config
    found = test_classes(scan())
    weights, heavy, unknown = weigh(found, config)
    return found, weights, heavy, unknown, pack(weights, heavy, count)


def check(count, tests_file=None):
    found, weights, heavy, unknown, bins = build(count)
    filters = [shard_filter(bins, i) for i in range(count)]
    errors = []
    for fqn in sorted(found):
        sample = token(fqn) + 'SomeTest'
        hits = [i for i, f in enumerate(filters) if matches(f, sample)]
        if len(hits) != 1:
            errors.append(f'{fqn}: in shards {hits}')
    # A test the scanner does not know about must still run somewhere, and only once.
    stray = f'{ASSEMBLY_PREFIX}.Tests.SWUnscannedTestClass.SomeTest'
    hits = [i for i, f in enumerate(filters) if matches(f, stray)]
    if hits != [count - 1]:
        errors.append(f'unknown class lands in shards {hits}, expected [{count - 1}]')
    if tests_file:
        # Real names from `dotnet vstest <dll> --ListFullyQualifiedTests --ListTestsTargetPath:<file>`.
        with open(tests_file, encoding='utf-8-sig') as f:
            names = [line.strip() for line in f if line.strip()]
        per_shard = [0] * count
        caught = set()
        for name in names:
            hits = [i for i, f in enumerate(filters) if matches(f, name)]
            if len(hits) != 1:
                errors.append(f'test {name}: in shards {hits}')
                continue
            per_shard[hits[0]] += 1
            if hits[0] == count - 1 and not any(name.startswith(token(c)) for c in found):
                caught.add(name.split('(')[0].rsplit('.', 1)[0])
        print(f'{len(names)} listed tests per shard: {per_shard}')
        for c in sorted(caught):
            print(f'note: {c} is not found by the scanner; it runs in the catch-all shard')
    heavy_bins = [sum(1 for f in b['classes'] if f in heavy) for b in bins]
    if len(heavy) <= count and any(h > 1 for h in heavy_bins):
        errors.append(f'heavy classes share a shard: {heavy_bins}')
    for name in unknown:
        errors.append(f'{os.path.basename(WEIGHTS_FILE)} names {name}, which is not a test class')
    print(f'{len(found)} test classes, {sum(t for t, _ in found.values())} tests + '
          f'{sum(s for _, s in found.values())} case sources, {count} shards')
    for i, b in enumerate(bins):
        print(f'shard {i}: {len(b["classes"]):3} classes, weight {b["load"]:7.0f}, '
              f'heavy {heavy_bins[i]}, filter {len(filters[i])} chars')
    for e in errors:
        print('ERROR', e)
    print('check ' + ('failed' if errors else 'passed'))
    return not errors


def main():
    parser = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    parser.add_argument('--index', type=int, help='shard to print the filter for, 0-based')
    parser.add_argument('--count', type=int, required=True, help='number of shards')
    parser.add_argument('--check', action='store_true', help='assert every test class lands in exactly one shard')
    parser.add_argument('--tests', help='with --check: file of fully qualified test names to check as well')
    parser.add_argument('--list', action='store_true', help='print the shards with their classes and weights')
    args = parser.parse_args()
    if args.count < 1:
        parser.error('--count must be at least 1')

    if args.check:
        sys.exit(0 if check(args.count, args.tests) else 1)

    found, weights, heavy, unknown, bins = build(args.count)
    if args.list:
        for i, b in enumerate(bins):
            print(f'shard {i}: weight {b["load"]:.0f}{"  (catch-all)" if i == args.count - 1 else ""}')
            for f in sorted(b['classes'], key=lambda f: -weights[f]):
                print(f'  {weights[f]:7.0f}  {f}{"  [heavy]" if f in heavy else ""}')
        return
    if args.index is None or not 0 <= args.index < args.count:
        parser.error('--index must be in [0, count)')
    print(shard_filter(bins, args.index))


if __name__ == '__main__':
    main()
