"""Spike S10: transitive NuGet license report.

Usage: python3 -I license_report.py <dotnet-list-package.json> <nuget-packages-dir>
Input is `dotnet list <proj> package --include-transitive --format json`.
Reads each package's .nuspec for <license type="expression"> or <licenseUrl>.
Prints a Markdown table; flags anything outside the allowlist. Report only (exit 0).
"""
import json
import os
import sys
import xml.etree.ElementTree as ET

ALLOW = {"MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "MS-PL", "0BSD", "ISC", "PostgreSQL"}


def nuspec_license(pkg_dir, pid, ver):
    path = os.path.join(pkg_dir, pid.lower(), ver.lower(), f"{pid.lower()}.nuspec")
    if not os.path.exists(path):
        return "nuspec not found"
    root = ET.parse(path).getroot()
    for el in root.iter():
        tag = el.tag.split("}")[-1]
        if tag == "license":
            return f"{el.get('type', '?')}:{(el.text or '').strip()}"
    for el in root.iter():
        if el.tag.split("}")[-1] == "licenseUrl":
            return f"url:{(el.text or '').strip()}"
    return "none declared"


def main(list_json, pkg_dir):
    data = json.load(open(list_json, encoding="utf-8"))
    seen = {}
    for proj in data.get("projects", []):
        for fw in proj.get("frameworks", []):
            for kind in ("topLevelPackages", "transitivePackages"):
                for p in fw.get(kind, []):
                    seen[(p["id"], p["resolvedVersion"])] = kind
    print("| package | version | kind | license | flag |")
    print("|---|---|---|---|---|")
    for (pid, ver), kind in sorted(seen.items(), key=lambda x: x[0][0].lower()):
        lic = nuspec_license(pkg_dir, pid, ver)
        expr = lic.split(":", 1)[1] if lic.startswith("expression:") else None
        ok = expr is not None and all(part.strip("() ") in ALLOW for part in expr.replace(" OR ", " ").replace(" AND ", " ").split())
        print(f"| {pid} | {ver} | {kind[:-8]} | {lic} | {'' if ok else 'REVIEW'} |")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
