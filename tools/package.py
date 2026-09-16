#!/usr/bin/env python3
"""Build every mod, write the dll sha256 into its manifest, and zip each mod into dist/.

    tools/package.py [--pack <BepInEx folder>] [--only Name]

The zip is reproducible: fixed timestamps, sorted entries, deflate. The printed lines are
the registry entries (registry.json shape) for the built zips.
"""
import argparse, hashlib, json, os, subprocess, sys, zipfile, io

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MODS = ["ServerMultipliers", "MotdAnnounce", "ClientHud"]
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
    a = ap.parse_args()
    os.makedirs(os.path.join(ROOT, "dist"), exist_ok=True)
    entries = []
    for name in MODS:
        if a.only and a.only != name:
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
        man["sha256"] = sha256(dll)
        man_text = json.dumps(man, indent=2) + "\n"
        with open(os.path.join(d, "waygate-mod.json"), "w", encoding="utf-8") as f:
            f.write(man_text)
        zip_name = "%s-%s.zip" % (man["id"], man["version"])
        zip_path = os.path.join(ROOT, "dist", zip_name)
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
        entries.append(entry)
        print("%s -> dist/%s (%d bytes) zip sha256 %s dll sha256 %s" % (name, zip_name, entry["size"], entry["sha256"], man["sha256"]))
    with open(os.path.join(ROOT, "dist", "entries.json"), "w", encoding="utf-8") as f:
        json.dump(entries, f, indent=2)
        f.write("\n")
    print("registry entries written to dist/entries.json")

if __name__ == "__main__":
    main()
