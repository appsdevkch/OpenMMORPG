"""Repository checks that need no Unity install.

Catches the quiet failures in a Unity repo distributed as a .unitypackage:

  * an asset with no .meta is skipped by the exporter, so it silently never ships
  * an orphan .meta is a file someone deleted without its metadata
  * two assets sharing a GUID breaks references in ways that are painful to trace
  * names differing only in case break checkouts on Windows and macOS
  * an AssetOrigin block in a .meta makes the Package Manager list the kit as
    whichever Asset Store product stamped it
  * a ThirdParty component missing from Third-Party Notices.txt fails Asset Store
    review, which requires the notices file
  * a model with no prefab in a Prefabs folder fails Asset Store review
    (guideline 2.4.b); collision meshes are exempt
  * a script that declares a type outside any namespace fails Asset Store review
    (guideline 2.5.a)

usage: check_repo.py [repo_root]
"""
import collections
import os
import re
import sys

NOTICES = "Third-Party Notices.txt"
THIRD_PARTY = "ThirdParty"
MODEL_EXTENSIONS = (".fbx", ".obj", ".blend", ".dae", ".3ds", ".glb", ".gltf")
GUID = re.compile(r"guid: ([0-9a-f]{32})")
TYPE_DECLARATION = re.compile(r"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|static|sealed|abstract|partial|readonly)\s+)*"
                              r"(?:class|struct|interface|enum|record)\s+\w", re.M)
NAMESPACE = re.compile(r"^\s*namespace\s+[\w.]+", re.M)

# Unity ignores these, so they are not part of the product.
def ignored(rel):
    parts = rel.split("/")
    return any(p == ".git" or p == ".github" or p.endswith("~") or p.startswith(".") for p in parts)


def walk(root):
    """Every asset path and every .meta path, Unity's view of the tree."""
    assets, metas = set(), set()
    for dirpath, dirnames, filenames in os.walk(root):
        rel_dir = os.path.relpath(dirpath, root).replace("\\", "/")
        if rel_dir == ".":
            rel_dir = ""
        dirnames[:] = [d for d in dirnames if not ignored((rel_dir + "/" + d).lstrip("/"))]

        for d in dirnames:
            assets.add((rel_dir + "/" + d).lstrip("/"))
        for fn in filenames:
            rel = (rel_dir + "/" + fn).lstrip("/")
            if ignored(rel):
                continue
            if rel.endswith(".meta"):
                metas.add(rel[:-5])
            else:
                assets.add(rel)
    return assets, metas


def read_metas(root, metas):
    """GUID -> .meta files, plus the .meta files carrying an Asset Store origin stamp."""
    guids = collections.defaultdict(list)
    stamped = []
    for rel in metas:
        path = os.path.join(root, rel + ".meta")
        try:
            with open(path, encoding="utf-8", errors="replace") as f:
                for line in f:
                    if line.startswith("guid: "):
                        guids[line[6:].strip()].append(rel + ".meta")
                    elif line.startswith("AssetOrigin:"):
                        stamped.append(rel + ".meta")
        except OSError:
            continue
    return guids, sorted(stamped)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    failures = []

    assets, metas = walk(root)
    print(f"assets: {len(assets)} | meta files: {len(metas)}")

    missing = sorted(assets - metas)
    if missing:
        failures.append(f"{len(missing)} asset(s) have no .meta, so they would not ship:\n  " + "\n  ".join(missing[:20]))

    orphans = sorted(metas - assets)
    if orphans:
        failures.append(f"{len(orphans)} orphan .meta file(s), the asset is gone:\n  " + "\n  ".join(o + ".meta" for o in orphans[:20]))

    guids, stamped = read_metas(root, metas)
    dupes = {g: f for g, f in guids.items() if len(f) > 1}
    if dupes:
        lines = [f"  {g}: {', '.join(f[:3])}" for g, f in list(dupes.items())[:10]]
        failures.append(f"{len(dupes)} duplicate GUID(s):\n" + "\n".join(lines))
    print(f"unique GUIDs: {len(guids)}")

    # The Asset Store stamps downloaded .meta files with the product they came from, and the
    # Package Manager lists whatever product those stamps name. A stamp carried over from
    # another asset makes the kit show up as that asset.
    if stamped:
        failures.append(
            f"{len(stamped)} .meta file(s) carry an AssetOrigin block from another Asset Store product:\n  "
            + "\n  ".join(stamped[:20]))

    lowered = collections.defaultdict(list)
    for a in assets:
        lowered[a.lower()].append(a)
    clashes = {k: v for k, v in lowered.items() if len(v) > 1}
    if clashes:
        lines = [f"  {', '.join(v)}" for v in list(clashes.values())[:10]]
        failures.append(f"{len(clashes)} name(s) differing only in case:\n" + "\n".join(lines))

    # Every bundled third-party component has to be named in the notices file.
    notices_path = os.path.join(root, NOTICES)
    third_party_dir = os.path.join(root, THIRD_PARTY)
    if not os.path.isfile(notices_path):
        failures.append(f"{NOTICES} is missing; Asset Store review requires it")
    elif os.path.isdir(third_party_dir):
        with open(notices_path, encoding="utf-8", errors="replace") as f:
            notices = f.read()
        undocumented = sorted(
            name for name in os.listdir(third_party_dir)
            if os.path.isdir(os.path.join(third_party_dir, name)) and name not in notices
        )
        if undocumented:
            failures.append(
                f"{len(undocumented)} ThirdParty component(s) absent from {NOTICES}:\n  " + "\n  ".join(undocumented))
        else:
            print(f"third-party components documented: {len(os.listdir(third_party_dir))} entries checked")

    # Every model needs a prefab of its own, in a Prefabs folder.
    def meta_guid(asset):
        with open(os.path.join(root, asset + ".meta"), encoding="utf-8", errors="replace") as f:
            return GUID.search(f.read(4096)).group(1)
    models = {meta_guid(a): a for a in assets
              if a.lower().endswith(MODEL_EXTENSIONS) and "/Collisions/" not in a}
    referenced = set()
    for a in assets:
        if a.endswith(".prefab") and "/Prefabs/" in "/" + a:
            with open(os.path.join(root, a), encoding="utf-8", errors="replace") as f:
                referenced.update(g for g in GUID.findall(f.read()) if g in models)
    without_prefab = sorted(models[g] for g in models if g not in referenced)
    if without_prefab:
        failures.append(f"{len(without_prefab)} model(s) with no prefab in a Prefabs folder:\n  "
                        + "\n  ".join(without_prefab[:20]))
    else:
        print(f"models with a prefab: {len(models)} checked")

    # Every script declares its types inside a namespace.
    outside = []
    for a in assets:
        if a.endswith(".cs"):
            with open(os.path.join(root, a), encoding="utf-8-sig", errors="replace") as f:
                code = re.sub(r"//[^\n]*|/\*.*?\*/", "", f.read(), flags=re.S)
            if TYPE_DECLARATION.search(code) and not NAMESPACE.search(code):
                outside.append(a)
    if outside:
        failures.append(f"{len(outside)} script(s) declaring types outside a namespace:\n  "
                        + "\n  ".join(sorted(outside)[:20]))
    else:
        print("scripts in a namespace: all")

    if failures:
        print("\nFAILED")
        for f in failures:
            print("\n" + f)
        return 1

    print("all repository checks passed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
