#!/usr/bin/env python3
"""Build every mod, write the dll sha256 into its manifest, and zip each mod into dist/.

    tools/package.py [--pack <BepInEx folder>] [--only Name[,Name]] [--allow-changed]

The zip is reproducible: fixed timestamps, sorted entries, deflate. The printed lines are
the registry entries (registry.json shape) for the built zips.
"""
import argparse, hashlib, json, os, subprocess, sys, zipfile, io

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODS = ["ServerMultipliers", "MotdAnnounce", "ClientHud", "Chronicle", "DarkNights", "WaygateTravel", "HordeNights"]
FIXED = (2026, 1, 1, 0, 0, 0)

def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 16), b""):
            h.update(chunk)
    return h.hexdigest()

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pack", default=os.environ.get("WAYGATE_PACK", ""))
    ap.add_argument("--only", default="")
    ap.add_argument("--no-build", action="store_true")
    ap.add_argument("--allow-changed", action="store_true", help="overwrite a dist zip whose bytes would change for a version that already exists (never do this for a released version: the registry and hosting panels pin its hash)")
    a = ap.parse_args()
    os.makedirs(os.path.join(ROOT, "dist"), exist_ok=True)
    entries = []
    kept = {}
    try:
        with open(os.path.join(ROOT, "dist", "entries.json"), "r", encoding="utf-8") as f:
            kept = {e["id"]: e for e in json.load(f)}
    except (OSError, ValueError):
        kept = {}
    only = [n.strip() for n in a.only.split(",") if n.strip()]
    for name in MODS:
        if only and name not in only:
            continue
        d = os.path.join(ROOT, name)
        if not a.no_build:
            cmd = ["dotnet", "build", os.path.join(d, name + ".csproj"), "-c", "Release", "-nologo", "-v", "q"]
            if a.pack:
                cmd.append("-p:PackDir=" + a.pack)
            r = subprocess.run(cmd, capture_output=True, text=True)
            if r.returncode != 0:
                print(r.stdout[-3000:], r.stderr[-1000:])
                sys.exit("build failed: " + name)
        with open(os.path.join(d, "waygate-mod.json"), "r", encoding="utf-8") as f:
            man = json.load(f)
        dll = os.path.join(d, "bin", "Release", man["dll"])
        if not os.path.exists(dll):
            sys.exit("no dll at " + dll)
        zip_name = "%s-%s.zip" % (man["id"], man["version"])
        zip_path = os.path.join(ROOT, "dist", zip_name)
        # A zip that exists for this version may already be released: the registry, the app
        # and hosting panels pin its hash. A rebuild whose dll differs (another SDK patch is
        # enough) must become a new version, never new bytes under the old name.
        if os.path.exists(zip_path) and not a.allow_changed:
            with zipfile.ZipFile(zip_path) as old:
                old_dll = hashlib.sha256(old.read(man["dll"])).hexdigest()
            if old_dll != sha256(dll):
                print("%s: dist/%s exists and this build's dll differs (%s.. vs %s..). Left untouched. Bump the version, or pass --allow-changed for a version that was never released." % (name, zip_name, old_dll[:12], sha256(dll)[:12]))
                continue
        man["sha256"] = sha256(dll)
        man_text = json.dumps(man, indent=2) + "\n"
        with open(os.path.join(d, "waygate-mod.json"), "w", encoding="utf-8") as f:
            f.write(man_text)
        files = [("waygate-mod.json", man_text.encode("utf-8")),
                 (man["dll"], open(dll, "rb").read()),
                 ("README.md", open(os.path.join(d, "README.md"), "rb").read()),
                 ("LICENSE", open(os.path.join(d, "LICENSE"), "rb").read())]
        files.sort(key=lambda t: t[0])
        buf = io.BytesIO()
        with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as z:
            for fn, data in files:
                zi = zipfile.ZipInfo(fn, date_time=FIXED)
                zi.compress_type = zipfile.ZIP_DEFLATED
                zi.external_attr = 0o644 << 16
                z.writestr(zi, data)
        with open(zip_path, "wb") as f:
            f.write(buf.getvalue())
        entry = {
            "id": man["id"], "name": man["name"], "version": man["version"], "author": man["author"],
            "side": man["side"], "game_build": man["game_build"], "dll": man["dll"], "dll_sha256": man["sha256"],
            "sha256": sha256(zip_path), "size": os.path.getsize(zip_path),
            "url": "https://github.com/HumanGenome/WaygateMods-first/releases/download/%s-v%s/%s" % (man["id"], man["version"], zip_name),
            "source": man.get("source", ""), "license": man.get("license", "MIT"),
            "description": man.get("description", ""), "dependencies": man.get("dependencies", []), "revoked": False,
        }
        kept[entry["id"]] = entry
        entries.append(entry)
        print("%s -> dist/%s (%d bytes) zip sha256 %s dll sha256 %s" % (name, zip_name, entry["size"], entry["sha256"], man["sha256"]))
    # entries.json keeps every mod: the ones built now replace their line, the rest stay as they were
    order = {("HumanGenome-" + n): i for i, n in enumerate(MODS)}
    merged = sorted(kept.values(), key=lambda e: order.get(e["id"], 999))
    with open(os.path.join(ROOT, "dist", "entries.json"), "w", encoding="utf-8") as f:
        json.dump(merged, f, indent=2)
        f.write("\n")
    print("registry entries written to dist/entries.json")

if __name__ == "__main__":
    main()
