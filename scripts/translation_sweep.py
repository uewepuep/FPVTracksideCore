#!/usr/bin/env python3
"""Translation sweep for UI/Translations.xlsx.

  report  Find keys the code asks for that the sheet doesn't have, plus existing rows with
          empty language cells, and write them to a work file (TSV) to be filled in.
  apply   Merge a filled-in work file back into the sheet. Existing non-empty cells are
          never overwritten unless --overwrite is given.
  remove  Remove rows the code no longer uses.

Keys come from two places:
  - a static scan of the C# source (Translator.Get literals, buttons, menus, tabs,
    settings editor categories and property names)
  - optionally the app's runtime record (FPVTRACKSIDE_TRANSLATION_SWEEP=1 writes
    TranslationsMissing.tsv to the app's working directory), which catches keys
    built at runtime that the static scan can't see.

The uewepuep (upside-down) column is generated automatically, using a character map
learned from the rows already in the sheet.

Standard library only, so it runs anywhere python3 does.
"""

import argparse
import collections
import os
import re
import sys
import xml.etree.ElementTree as ET
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_XLSX = os.path.join(REPO, "UI", "Translations.xlsx")
# The outer repo (UIExtra, ThreeDee, FPVWebsite...) sits next to Core and has its own sheet, loaded after Core's
APP_REPO = os.path.join(os.path.dirname(REPO), "FPVTrackside")
DEFAULT_EXTRA_XLSX = os.path.join(APP_REPO, "UIExtra", "TranslationsExtra.xlsx")
DEFAULT_RUNTIME = os.path.join(os.path.expanduser("~"), "Documents", "FPVTrackside", "TranslationsMissing.tsv")
SHEET = "xl/worksheets/sheet1.xml"
NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
M = "{" + NS + "}"
XML_SPACE = "{http://www.w3.org/XML/1998/namespace}space"
UPSIDE_DOWN = "uewepuep"
SKIP_DIRS = {"bin", "obj", "node_modules", ".git", ".vs", "Tests", "UnitTests", "MonoGameTest", "ConsoleTest"}

ET.register_namespace("", NS)


# ---------------------------------------------------------------- keys

def normalise_key(key):
    # Matches Translator.NormaliseKey: only letters, digits and dots
    return "".join(c for c in key.replace("\\n", "") if c.isalnum() or c == ".")


def sheet_text(value):
    # The sheet stores line breaks as a literal \n
    return value.replace("\r\n", "\n").replace("\n", "\\n")


# ---------------------------------------------------------------- xlsx

def col_letters(ref):
    return re.match(r"[A-Z]+", ref).group()


def col_index(letters):
    n = 0
    for ch in letters:
        n = n * 26 + ord(ch) - 64
    return n


def col_name(index):
    s = ""
    while index:
        index, rem = divmod(index - 1, 26)
        s = chr(65 + rem) + s
    return s


def cell_text(c):
    return "".join(t.text or "" for t in c.iter(M + "t"))


class Sheet:
    def __init__(self, path):
        self.path = path
        with zipfile.ZipFile(path) as z:
            self.root = ET.fromstring(z.read(SHEET))
        self.data = self.root.find(M + "sheetData")
        self.rows = list(self.data.findall(M + "row"))

        header = self.rows[0]
        self.languages = []  # (column index, name), English first
        for c in header.findall(M + "c"):
            ci = col_index(col_letters(c.get("r")))
            if ci > 1 and cell_text(c):
                self.languages.append((ci, cell_text(c)))

        self.by_key = {}  # normalised key -> row element
        self.keys = []    # key as written, in sheet order
        for row in self.rows[1:]:
            key = self.get(row, 1)
            if key:
                self.keys.append(key)
                self.by_key[normalise_key(key)] = row

    def cells(self, row):
        return {col_index(col_letters(c.get("r"))): c for c in row.findall(M + "c")}

    def get(self, row, ci):
        c = self.cells(row).get(ci)
        return cell_text(c) if c is not None else ""

    def values(self, row):
        cells = self.cells(row)
        return {name: (cell_text(cells[ci]) if ci in cells else "") for ci, name in self.languages}

    def set(self, row, ci, value):
        r = row.get("r")
        cells = self.cells(row)
        c = cells.get(ci)
        if c is None:
            c = ET.Element(M + "c", {"r": col_name(ci) + r, "t": "inlineStr"})
            # keep cells in column order
            after = [i for i in cells if i < ci]
            pos = list(row).index(cells[max(after)]) + 1 if after else 0
            row.insert(pos, c)
        for child in list(c):
            c.remove(child)
        c.set("t", "inlineStr")
        is_ = ET.SubElement(c, M + "is")
        t = ET.SubElement(is_, M + "t")
        t.text = value
        if value != value.strip():
            t.set(XML_SPACE, "preserve")

    def add_row(self, key):
        number = str(len(self.rows) + 1)
        row = ET.SubElement(self.data, M + "row", {"r": number})
        self.rows.append(row)
        self.set(row, 1, key)
        self.keys.append(key)
        self.by_key[normalise_key(key)] = row
        return row

    @staticmethod
    def create_like(template, path):
        """A new sheet with the template's header row (languages, styles) and no keys."""
        with zipfile.ZipFile(template) as src, zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as dst:
            for item in src.infolist():
                dst.writestr(item, src.read(item.filename))
        sheet = Sheet(path)
        for row in sheet.rows[1:]:
            sheet.data.remove(row)
        sheet.rows = sheet.rows[:1]
        sheet.keys, sheet.by_key = [], {}
        sheet.save()
        return sheet

    def remove_row(self, key):
        row = self.by_key.pop(normalise_key(key))
        self.data.remove(row)
        self.rows.remove(row)
        self.keys = [k for k in self.keys if normalise_key(k) != normalise_key(key)]

    def save(self):
        # Renumber rows so they stay contiguous after removals
        for i, row in enumerate(self.rows, 1):
            if row.get("r") != str(i):
                row.set("r", str(i))
                for c in row.findall(M + "c"):
                    c.set("r", col_letters(c.get("r")) + str(i))

        last = col_name(max(ci for ci, _ in self.languages))
        dim = self.root.find(M + "dimension")
        if dim is not None:
            dim.set("ref", "A1:%s%d" % (last, len(self.rows)))

        xml = ET.tostring(self.root, encoding="utf-8")
        tmp = self.path + ".tmp"
        with zipfile.ZipFile(self.path) as src, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED) as dst:
            for item in src.infolist():
                dst.writestr(item, xml if item.filename == SHEET else src.read(item.filename))
        os.replace(tmp, self.path)


# ---------------------------------------------------------------- upside down

LINEBREAK = "\x00"


def learn_flip(sheets):
    """Learn English -> upside-down characters from the existing uewepuep columns."""
    counts = collections.defaultdict(collections.Counter)
    for sheet in sheets:
        ud = next((ci for ci, n in sheet.languages if n == UPSIDE_DOWN), None)
        en = next((ci for ci, n in sheet.languages if n == "English"), None)
        if ud is not None and en is not None:
            learn_rows(sheet, en, ud, counts)
    return {a: c.most_common(1)[0][0] for a, c in counts.items()}


def learn_rows(sheet, en, ud, counts):
    for row in sheet.rows[1:]:
        e, f = sheet.get(row, en), sheet.get(row, ud)
        e, f = e.replace("\\n", LINEBREAK), f.replace("u\\", LINEBREAK).replace("\\n", LINEBREAK)
        if not e or len(e) != len(f):
            continue
        for a, b in zip(e, reversed(f)):
            counts[a][b] += 1


def flip(text, table):
    # Rotating 180 degrees reverses the line order and each line, i.e. the whole string.
    # Translator.Format placeholders like {0} move with the text but stay readable to string.Format.
    parts = re.split(r"(\{\d+(?:[,:][^}]*)?\})", text.replace("\\n", LINEBREAK))
    out = []
    for part in reversed(parts):
        if re.fullmatch(r"\{\d+(?:[,:][^}]*)?\}", part):
            out.append(part)
        else:
            out.append("".join(table.get(ch, ch) for ch in reversed(part)))
    return "".join(out).replace(LINEBREAK, "\\n")


# ---------------------------------------------------------------- source scan

CS_STRING = r'"((?:[^"\\\n]|\\.)*)"'


def unescape(s):
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t", "r": ""}.get(m.group(1), m.group(1)), s)


def camel_to_human(name):
    # Matches Tools.Maths.CamelCaseToHuman
    return re.sub(r"([a-z])([A-Z0-9]+)", r"\1 \2", name.replace("_", " ")).strip()


PATTERNS = [
    # Translator.Get("Key", "English") and Translator.Format("Key", "English {0}", ...)
    (re.compile(r'Translator\.(?:Get|Format)\(\s*' + CS_STRING + r'\s*,\s*' + CS_STRING), lambda m: (m.group(1), m.group(2))),
    # Translator.Get("Prefix." + "x" ...) is rare; prefixed wrappers below cover the common ones
    (re.compile(r'new\s+TextButtonNode\(\s*' + CS_STRING), lambda m: ("Button." + m.group(1), m.group(1))),
    (re.compile(r'new\s+IconButtonNode\([^;"]*?,\s*' + CS_STRING), lambda m: ("Button." + m.group(1), m.group(1))),
    (re.compile(r'\.AddTab\(\s*' + CS_STRING), lambda m: ("Button." + m.group(1), m.group(1))),
    (re.compile(r'\.(?:AddItem|AddSubmenu|AddCheckboxItem|AddItemConfirm)\(\s*' + CS_STRING), lambda m: ("Menu." + m.group(1), m.group(1))),
    (re.compile(r'\[Category\(\s*' + CS_STRING + r'\s*\)'), lambda m: ("EditorCategory." + m.group(1), m.group(1))),
]

CLASS_RE = re.compile(r'\b(?:class|struct)\s+(\w+)')
PROP_RE = re.compile(r'((?:\s*\[[^\]]*\]\s*)*)\s*public\s+(?!static|const|override\s+string\s+ToString)[\w<>\[\],\.\? ]+?\s+(\w+)\s*\{\s*get;\s*(?:private\s+|protected\s+|internal\s+)?set;')


def scan_source(roots, joined=None):
    found = {}  # normalised key -> (key, english, where)

    def add(key, english, where):
        key, english = unescape(key), unescape(english)
        if "{" in key:
            return
        # Keys are stored normalised; symbol-only text like "+" or "▲" needs no translation
        key = normalise_key(key)
        if not re.search(r"\w\.?\w*[A-Za-z]", key.split(".", 1)[-1]):
            return
        found.setdefault(key, (key, english, where))

    roots = [os.path.abspath(r) for r in ([roots] if isinstance(roots, str) else roots)]
    for dirpath, dirs, files in (w for root in roots for w in os.walk(root)):
        # Z* projects are developer tools (version sync, string finder...), not shown to users
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS and not d.endswith(".Tests") and not (dirpath in roots and d.startswith("Z"))]
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(dirpath, f)
            rel = os.path.relpath(path, os.path.dirname(REPO))
            with open(path, encoding="utf-8-sig", errors="replace") as fh:
                src = fh.read()

            for rx, keyfn in PATTERNS:
                for m in rx.finditer(src):
                    key, english = keyfn(m)
                    where = "%s:%d" % (rel, src.count("\n", 0, m.start()) + 1)
                    # "Clone Profile '" + name: the key includes the runtime part, so no sheet row can match
                    if src[m.end():].lstrip().startswith("+"):
                        if joined is not None:
                            joined.append((key, where))
                        continue
                    add(key, english, where)

            # Settings editors: properties with their own [Category] or [DisplayName] are shown as ClassName.Property
            if "[Category(" not in src and "[DisplayName(" not in src:
                continue
            classes = [(m.start(), m.group(1)) for m in CLASS_RE.finditer(src)]
            for m in PROP_RE.finditer(src):
                attrs, prop = m.group(1), m.group(2)
                if "Browsable(false)" in attrs.replace(" ", "") or not re.search(r"\b(Category|DisplayName)\(", attrs):
                    continue
                owner = [name for pos, name in classes if pos < m.start()]
                if not owner:
                    continue
                dn = re.search(r'DisplayName\(\s*' + CS_STRING, attrs)
                english = dn.group(1) if dn else camel_to_human(prop)
                add(owner[-1] + "." + prop, english, "%s:%d" % (rel, src.count("\n", 0, m.start(2)) + 1))
    return found


def read_runtime(path):
    found = {}
    if not path or not os.path.exists(path):
        return found
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            parts = line.rstrip("\r\n").split("\t")
            if parts and parts[0]:
                english = parts[1] if len(parts) > 1 else ""
                found.setdefault(normalise_key(parts[0]), (parts[0], english.replace("\\n", "\n"), "runtime"))
    return found


# ---------------------------------------------------------------- commands

class Sheets:
    """Core's sheet and the Extra repo's sheet. A key lives in one of them."""

    def __init__(self, args, create=False):
        self.paths = {"core": args.xlsx, "extra": args.extra_xlsx}
        self.sheets = {"core": Sheet(args.xlsx)}
        if os.path.exists(args.extra_xlsx):
            self.sheets["extra"] = Sheet(args.extra_xlsx)
        elif create and os.path.isdir(os.path.dirname(args.extra_xlsx)):
            self.sheets["extra"] = Sheet.create_like(args.xlsx, args.extra_xlsx)
            print("Created %s" % args.extra_xlsx)
        self.changed = set()
        self.languages = [n for _, n in self.sheets["core"].languages]

    def find(self, key):
        n = normalise_key(key)
        for name, sheet in self.sheets.items():
            if n in sheet.by_key:
                return name, sheet, sheet.by_key[n]
        return None, None, None

    def all_keys(self):
        for name, sheet in self.sheets.items():
            for key in sheet.keys:
                yield name, sheet, key

    def save(self):
        for name in self.changed:
            self.sheets[name].save()


def scan_homes(args, joined=None):
    """normalised key -> (key, english, where, home sheet). Core's code wins when both use a key."""
    found = {}
    for root in args.roots:
        home = "core" if os.path.abspath(root) == os.path.abspath(REPO) else "extra"
        for k, v in scan_source([root], joined).items():
            if k not in found or (home == "core" and found[k][3] == "extra"):
                found[k] = v + (home,)
    return found


def cmd_report(args):
    sheets = Sheets(args)
    table = learn_flip(sheets.sheets.values())
    names = sheets.languages

    joined = []
    found = scan_homes(args, joined)
    runtime = read_runtime(args.runtime)
    for k, v in runtime.items():
        found.setdefault(k, v + ("core",))

    new = sorted((v for k, v in found.items() if sheets.find(k)[0] is None), key=lambda v: v[0])

    out_rows = []
    for key, english, where, home in new:
        values = {n: "" for n in names}
        values["English"] = sheet_text(english)
        if UPSIDE_DOWN in values:
            values[UPSIDE_DOWN] = flip(values["English"], table)
        out_rows.append(("new", home, key, where, values))

    gaps = collections.Counter()
    if not args.new_only:
        for name, sheet, key in sheets.all_keys():
            values = sheet.values(sheet.by_key[normalise_key(key)])
            if not values.get("English"):
                continue
            empty = [n for n in names if not values.get(n)]
            if UPSIDE_DOWN in empty:
                values[UPSIDE_DOWN] = flip(values["English"], table)
                empty.remove(UPSIDE_DOWN)
            if empty:
                gaps.update(empty)
                out_rows.append(("gap", name, key, "", values))

    with open(args.out, "w", encoding="utf-8", newline="") as fh:
        fh.write("\t".join(["Status", "Sheet", "Name", "Source"] + names) + "\n")
        for status, home, key, where, values in out_rows:
            fh.write("\t".join([status, home, key, where] + [values.get(n, "").replace("\t", " ") for n in names]) + "\n")

    for name, sheet in sheets.sheets.items():
        print("Sheet %s: %d keys, %d languages (%s)" % (name, len(sheet.keys), len(names), sheets.paths[name]))
    if "extra" not in sheets.sheets:
        print("No extra sheet yet at %s - apply creates it when a row needs it" % args.extra_xlsx)
    print("Source scan: %d keys, runtime record: %d keys (%s)" % (len(found) - len([k for k in runtime if k not in found]), len(runtime), args.runtime if runtime else "none"))
    print("New keys: %d" % len(new))
    for key, english, where, home in new:
        print("  %-4s %-55s %-40r %s" % (home, key, english, where))
    if not args.new_only:
        print("Existing rows with empty cells: %d" % sum(1 for r in out_rows if r[0] == "gap"))
        for name, n in gaps.most_common():
            print("  %-12s %d" % (name, n))
    if joined:
        print("Text joined with + before translating, so it can't be translated (needs a code change): %d" % len(joined))
        for key, where in joined:
            print("  %-60s %s" % (key, where))
    misplaced = [(name, key, found[normalise_key(key)][3]) for name, sheet, key in sheets.all_keys()
                 if normalise_key(key) in found and found[normalise_key(key)][3] != name]
    if misplaced:
        print("Rows in the other repo's sheet (move --auto fixes): %d" % len(misplaced))
        for name, key, home in misplaced:
            print("  %s -> %s  %s" % (name, home, key))
    unused = [key for name, sheet, key in sheets.all_keys() if normalise_key(key) not in found]
    print("Sheet keys not found in the scan (may be built at runtime, or unused): %d" % len(unused))
    print("Wrote %s" % args.out)


PLACEHOLDER = re.compile(r"\{\d+(?:[,:][^}]*)?\}")


def placeholders(text):
    return sorted(PLACEHOLDER.findall(text))


def cmd_apply(args):
    sheets = Sheets(args, create=True)
    table = learn_flip(sheets.sheets.values())

    added = filled = skipped = 0
    with open(args.work, encoding="utf-8") as fh:
        rows = [line.rstrip("\n").split("\t") for line in fh if line.strip()]
    header = rows[0]
    for parts in rows[1:]:
        rec = dict(zip(header, parts + [""] * (len(header) - len(parts))))
        key = rec.get("Name", "")
        if not key:
            continue
        if not rec.get("English"):
            print("skipping %s: no English" % key)
            continue
        if UPSIDE_DOWN in sheets.languages and not rec.get(UPSIDE_DOWN):
            rec[UPSIDE_DOWN] = flip(rec["English"], table)

        name, sheet, row = sheets.find(key)
        if row is None:
            if rec.get("Status") != "new" and not args.add_unmarked:
                print("skipping %s: not in a sheet and not marked new" % key)
                continue
            name = rec.get("Sheet") or "core"
            if name not in sheets.sheets:
                print("skipping %s: no %s sheet" % (key, name))
                continue
            sheet = sheets.sheets[name]
            row = sheet.add_row(normalise_key(key))
            sheets.changed.add(name)
            added += 1

        for ci, lang in sheet.languages:
            value = rec.get(lang, "")
            if not value:
                continue
            # Translator.Format text: a lost or changed {0} would break the runtime value
            if lang != UPSIDE_DOWN and placeholders(value) != placeholders(rec["English"]):
                print("skipping %s %s: placeholders %s don't match the English %s" % (key, lang, placeholders(value), placeholders(rec["English"])))
                continue
            current = sheet.get(row, ci)
            if current and not args.overwrite:
                if current != value:
                    skipped += 1
                continue
            if current != value:
                sheet.set(row, ci, value)
                sheets.changed.add(name)
                filled += 1

    # Older upside-down rows reversed "\n" into "u\" - put the line break back
    repaired = 0
    for name, sheet in sheets.sheets.items():
        cols = {n: ci for ci, n in sheet.languages}
        if UPSIDE_DOWN not in cols:
            continue
        for row in sheet.rows[1:]:
            e, f = sheet.get(row, cols["English"]), sheet.get(row, cols[UPSIDE_DOWN])
            if "\\n" in e and "u\\" in f and e.count("\\n") == f.count("u\\"):
                sheet.set(row, cols[UPSIDE_DOWN], f.replace("u\\", "\\n"))
                sheets.changed.add(name)
                repaired += 1

    if args.dry_run:
        print("Dry run: would add %d rows, set %d cells, repair %d upside-down line breaks" % (added, filled, repaired))
        return
    sheets.save()
    print("Added %d rows, set %d cells, repaired %d upside-down line breaks" % (added, filled, repaired))
    if skipped:
        print("Left %d existing cells that differed from the work file (use --overwrite to replace)" % skipped)


def cmd_remove(args):
    sheets = Sheets(args)
    for key in args.keys:
        name, sheet, row = sheets.find(key)
        if row is None:
            print("not in a sheet: %s" % key)
            continue
        if args.dry_run:
            print("would remove %s from %s" % (key, name))
            continue
        sheet.remove_row(key)
        sheets.changed.add(name)
        print("removed %s from %s" % (key, name))
    if not args.dry_run:
        sheets.save()


def cmd_move(args):
    sheets = Sheets(args, create=True)
    if args.auto:
        found = scan_homes(args)
        moves = [(key, found[normalise_key(key)][3]) for name, sheet, key in sheets.all_keys()
                 if normalise_key(key) in found and found[normalise_key(key)][3] != name]
    else:
        moves = [(key, args.to) for key in args.keys]

    for key, to in moves:
        name, sheet, row = sheets.find(key)
        if row is None or name == to:
            print("nothing to move: %s" % key)
            continue
        if to not in sheets.sheets:
            print("no %s sheet for %s" % (to, key))
            continue
        values = sheet.values(row)
        target = sheets.sheets[to]
        new_row = target.add_row(normalise_key(key))
        for ci, lang in target.languages:
            if values.get(lang):
                target.set(new_row, ci, values[lang])
        sheet.remove_row(key)
        sheets.changed.update([name, to])
        print("moved %s: %s -> %s" % (key, name, to))
    if not args.dry_run:
        sheets.save()
    else:
        print("Dry run: nothing saved")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--xlsx", default=DEFAULT_XLSX, help="Core's sheet")
    p.add_argument("--extra-xlsx", default=DEFAULT_EXTRA_XLSX, help="the Extra repo's sheet (loaded after Core's)")
    p.add_argument("--roots", nargs="+", default=[REPO] + ([APP_REPO] if os.path.isdir(APP_REPO) else []),
                   help="source trees to scan (default: Core and, if present, the FPVTrackside app repo next to it)")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("report", help="find missing keys and gaps, write a work file")
    r.add_argument("--runtime", default=DEFAULT_RUNTIME, help="TranslationsMissing.tsv from a sweep run of the app")
    r.add_argument("--out", default="TranslationsSweep.tsv")
    r.add_argument("--new-only", action="store_true", help="only new keys, not gaps in existing rows")
    r.set_defaults(fn=cmd_report)

    a = sub.add_parser("apply", help="merge a filled-in work file into the sheets")
    a.add_argument("work")
    a.add_argument("--overwrite", action="store_true", help="replace existing non-empty cells")
    a.add_argument("--add-unmarked", action="store_true", help="add rows not in a sheet even if Status isn't 'new'")
    a.add_argument("--dry-run", action="store_true")
    a.set_defaults(fn=cmd_apply)

    d = sub.add_parser("remove", help="remove rows the code no longer uses")
    d.add_argument("keys", nargs="+")
    d.add_argument("--dry-run", action="store_true")
    d.set_defaults(fn=cmd_remove)

    m = sub.add_parser("move", help="move rows to the sheet of the repo that uses them")
    m.add_argument("keys", nargs="*")
    m.add_argument("--to", choices=["core", "extra"])
    m.add_argument("--auto", action="store_true", help="move every row the scan says belongs in the other sheet")
    m.add_argument("--dry-run", action="store_true")
    m.set_defaults(fn=cmd_move)

    args = p.parse_args()
    if args.cmd == "move" and not args.auto and not (args.keys and args.to):
        p.error("move needs --auto, or keys and --to")
    args.fn(args)


if __name__ == "__main__":
    main()
