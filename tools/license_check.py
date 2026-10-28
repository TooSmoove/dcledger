"""Fails when a dependency's license isn't allowed by tools/license-policy.json.

NuGet: every package resolved in any packages.lock.json under src/ and tests/, with the license read
from its .nuspec in the local package folder (run after `dotnet restore`).
npm: every non-dev package in site/package-lock.json (lockfile v3 records each package's license).

Usage:
  python3 tools/license_check.py --policy tools/license-policy.json --repo . \
      --nuget-packages ~/.nuget/packages --npm-lock site/package-lock.json
Exit code 0 when everything is allowed, 1 otherwise. Standard library only.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


@dataclass(frozen=True)
class Package:
    ecosystem: str  # "nuget" or "npm"
    name: str
    version: str
    license: str  # an SPDX expression, or "file:<name>", "url:<url>", "none"


def license_allowed(package: Package, policy: dict) -> bool:
    """True when the package's SPDX expression is satisfied by the allowed set plus scoped exceptions.

    An exception's license is either an extra SPDX id for the matching packages, or the exact non-SPDX value a
    package declares (for example "url:https://..."), recorded after someone read that license text.
    """
    allowed = set(policy["allowed"])
    for exception in policy.get("exceptions", []):
        if exception["ecosystem"] == package.ecosystem and fnmatch.fnmatchcase(package.name, exception["package"]):
            if exception["license"] == package.license:
                return True
            allowed.add(exception["license"])
    return expression_satisfied(package.license, allowed)


def expression_satisfied(expression: str, allowed: set[str]) -> bool:
    """Evaluates a simple SPDX expression: alternatives joined by OR, each a set of terms joined by AND."""
    if not expression or expression.startswith(("file:", "url:")) or expression == "none":
        return False
    flat = expression.replace("(", " ").replace(")", " ")
    alternatives = re.split(r"\s+OR\s+", flat.strip())
    return any(
        all(term in allowed for term in re.split(r"\s+AND\s+", alternative.strip()))
        for alternative in alternatives
    )


def nuget_packages(repo: Path, packages_folder: Path) -> list[Package]:
    found: dict[tuple[str, str], Package] = {}
    lock_files = sorted([*repo.glob("src/*/packages.lock.json"), *repo.glob("tests/*/packages.lock.json")])
    for lock_file in lock_files:
        lock = json.loads(lock_file.read_text(encoding="utf-8"))
        for target in lock.get("dependencies", {}).values():
            for name, entry in target.items():
                if entry.get("type") == "Project" or "resolved" not in entry:
                    continue
                key = (name.lower(), entry["resolved"].lower())
                if key not in found:
                    found[key] = Package("nuget", name, entry["resolved"], nuspec_license(packages_folder, *key))
    return sorted(found.values(), key=lambda p: (p.name.lower(), p.version))


def nuspec_license(packages_folder: Path, name: str, version: str) -> str:
    nuspec = packages_folder / name / version / f"{name}.nuspec"
    if not nuspec.exists():
        return "none"
    root = ET.parse(nuspec).getroot()
    for element in root.iter():
        tag = element.tag.rsplit("}", 1)[-1]
        if tag == "license":
            kind = element.get("type", "")
            text = (element.text or "").strip()
            return text if kind == "expression" else f"{kind or 'file'}:{text}"
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] == "licenseUrl":
            return f"url:{(element.text or '').strip()}"
    return "none"


def npm_packages(lock_path: Path) -> list[Package]:
    lock = json.loads(lock_path.read_text(encoding="utf-8"))
    packages = []
    for path, entry in lock.get("packages", {}).items():
        if not path or entry.get("link") or entry.get("dev") or entry.get("devOptional"):
            continue
        name = entry.get("name") or path.rsplit("node_modules/", 1)[-1]
        packages.append(Package("npm", name, entry.get("version", "?"), str(entry.get("license") or "none")))
    return sorted(packages, key=lambda p: (p.name, p.version))


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--policy", type=Path, required=True)
    parser.add_argument("--repo", type=Path, default=Path("."))
    parser.add_argument("--nuget-packages", type=Path, required=True)
    parser.add_argument("--npm-lock", type=Path, required=True)
    args = parser.parse_args(argv)

    policy = json.loads(args.policy.read_text(encoding="utf-8"))
    packages = [*nuget_packages(args.repo, args.nuget_packages), *npm_packages(args.npm_lock)]
    rejected = [p for p in packages if not license_allowed(p, policy)]

    print(f"Checked {len(packages)} packages against {args.policy}.")
    for p in rejected:
        message = f"{p.ecosystem} {p.name} {p.version}: license '{p.license}' is not allowed by {args.policy}"
        print(f"::error title=License not allowed::{message}")
    return 1 if rejected else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
