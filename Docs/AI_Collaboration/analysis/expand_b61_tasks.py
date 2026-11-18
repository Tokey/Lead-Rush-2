#!/usr/bin/env python3
"""Expand B61's sourced workstream templates into map/surface/weapon-scoped leaf tasks."""
import csv
import re
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPECS = ROOT / "specs"
OUTPUT = ROOT / "data" / "B61_task_register.csv"

MAPS = [("SAL", "Saltworks"), ("DSC", "Deadsector"), ("FND", "Foundry"), ("TR", "TestRange")]
SURFACES = [
    ("CONC", "Concrete"), ("ASPH", "Asphalt"), ("WOOD", "Wood"), ("FAB", "Fabric"),
    ("PLAS", "Plastic"), ("CMET", "Coated metal"), ("BMET", "Bare metal"), ("TERR", "Terrain"),
]
WEAPONS = [("AK12", "AK-12"), ("MK23", "Mk23"), ("AK74N", "AK-74N"), ("MK18", "Mk18"), ("FAL", "FAL")]
RIFLES = [item for item in WEAPONS if item[0] != "MK23"]

ENV_MAPS = {
    "B61-E-002", "B61-E-004", "B61-E-005", "B61-E-006", "B61-E-007", "B61-E-009",
    "B61-E-010", "B61-E-012", "B61-E-015", "B61-E-018", "B61-E-030", "B61-E-031",
    "B61-E-032", "B61-E-038", "B61-E-039", "B61-E-040", "B61-E-041", "B61-E-043",
    "B61-E-044", "B61-E-050", "B61-E-054", "B61-E-055", "B61-E-056",
}
ENV_SURFACES = {
    "B61-E-019", "B61-E-020", "B61-E-021", "B61-E-022", "B61-E-023", "B61-E-024",
    "B61-E-025", "B61-E-051",
}
CHAR_WEAPONS = {"C07", "C08", "C09", "C10", "C11", "C12", "C13", "C14", "C29", "C32", "C33"}
AUDIO_WEAPONS = {"A03", "A09", "A10", "A11", "A12", "A14"}
AUDIO_MAPS = {"A19", "A20", "A21", "A38", "A40", "A41"}
AUDIO_SURFACES = {"A17", "A28"}

LINKED_ENV = {
    "B61-E-006": "B1", "B61-E-007": "P3", "B61-E-009": "B42", "B61-E-038": "B40/B1",
    "B61-E-039": "B1/B40", "B61-E-040": "B40/B1", "B61-E-041": "B40/B1",
    "B61-E-051": "P2", "B61-E-052": "P2", "B61-E-053": "P2/B42", "B61-E-054": "B30",
    "B61-E-055": "B1", "B61-E-056": "P3",
}
LINKED_AUDIO = {
    "A19": "P1", "A20": "P1", "A21": "P1/B57", "A22": "P1/B57", "A26": "P1",
    "A28": "B5", "A29": "B9/B57", "A30": "B5", "A34": "B12", "A43": "B58/B57",
    "A44": "B58/B57", "A55": "B57", "A60": "B12/B36/B58/P1",
}

FIELDS = [
    "id", "domain", "phase", "priority", "parent_template", "map", "surface", "weapon",
    "owner", "support_owners", "related_tracker", "title", "acceptance", "depends_on",
    "status", "research_source",
]


def split_cells(line):
    return [cell.strip() for cell in line.strip().strip("|").split("|")]


def split_character_task(combined):
    # The character source uses one cell for task + acceptance; the first full sentence is the action.
    match = re.match(r"^(.*?\.)\s+([A-Z].*)$", combined)
    if match:
        return match.group(1), match.group(2)
    return combined, "Record the measured result and retain the evidence described by the source workstream."


def read_templates(filename, row_pattern, kind):
    result = []
    group = "Unsorted"
    for line in (SPECS / filename).read_text(encoding="utf-8-sig").splitlines():
        if line.startswith("### "):
            group = line[4:].strip()
        if not line.startswith("|") or not re.match(row_pattern, line):
            continue
        cells = split_cells(line)
        if kind == "environment":
            raw_id, title, owner, acceptance = cells[:4]
            linked = ""
        elif kind == "character":
            raw_id, owner, linked, combined = cells[:4]
            title, acceptance = split_character_task(combined)
        else:
            raw_id, owner, title, acceptance = cells[:4]
            linked = ""
        result.append({
            "raw_id": raw_id, "title": title, "owner_raw": owner, "linked": linked,
            "acceptance": acceptance, "group": group, "file": filename, "kind": kind,
        })
    return result


def primary_and_support(owner_raw):
    owners = [owner.strip() for owner in re.split(r"\s*\+\s*", owner_raw) if owner.strip()]
    return (owners[0] if owners else "Unassigned", ";".join(owners[1:]))


def priority(task):
    text = " ".join((task["group"], task["linked"], task["raw_id"], task["title"])).lower()
    if any(token in text for token in ("b59", "b58", "death", "clip", "baseline", "p0", "b1 readability")):
        return "P0"
    if any(token in text for token in ("p2", "p3", "ambience")):
        return "P2"
    return "P1"


def dependencies(task):
    raw_id = task["raw_id"]
    deps = []
    if task["kind"] == "environment" and raw_id in ENV_MAPS and raw_id not in {"B61-E-006", "B61-E-007"}:
        deps.append("B1 baseline; P3 baseline for costly effects")
    if task["kind"] == "environment" and raw_id == "B61-E-055":
        deps.append("All preceding visual tasks complete")
    if task["kind"] == "environment" and raw_id == "B61-E-056":
        deps.append("All preceding environment tasks complete")
    if task["kind"] == "character" and raw_id in {"C09", "C12", "C13", "C14", "C29", "C32", "C33"}:
        deps.append("Relevant B49/B55 weapon prefab build")
    if task["kind"] == "character" and raw_id in {"C20", "C21", "C22", "C23", "C24", "C25", "C27", "C28"}:
        deps.append("B60.1 motion baseline")
    if task["kind"] == "audio" and raw_id in {"A43", "A44"}:
        deps.append("B58 implementation ready")
    if task["kind"] == "audio" and raw_id == "A60":
        deps.append("B12/B36/B58/P1 evidence available")
    return "; ".join(deps) if deps else "None; claim only after the parent/current owner is ready"


def add(rows, task, *, task_id=None, variant="", map_name="", surface="", weapon="", suffix=""):
    owner, support = primary_and_support(task["owner_raw"])
    title = task["title"] + (f" — {variant}" if variant else "")
    acceptance = task["acceptance"] + (f" {suffix}" if suffix else "")
    linked = task["linked"]
    if not linked and task["kind"] == "environment":
        linked = LINKED_ENV.get(task["raw_id"], "")
    if not linked and task["kind"] == "audio":
        linked = LINKED_AUDIO.get(task["raw_id"], "")
    rows.append({
        "id": task_id or task["raw_id"],
        "domain": task["kind"], "phase": task["group"], "priority": priority(task),
        "parent_template": task["raw_id"], "map": map_name, "surface": surface, "weapon": weapon,
        "owner": owner, "support_owners": support, "related_tracker": linked, "title": title,
        "acceptance": acceptance, "depends_on": dependencies(task), "status": "OPEN",
        "research_source": f"specs/{task['file']}#{task['raw_id']}",
    })


def expand():
    environment = read_templates("B61_Environment_Research.md", r"^\| B61-E-\d{3} \|", "environment")
    character = read_templates("B61_Character_Weapon_Research.md", r"^\| C\d{2} \|", "character")
    audio = read_templates("B61_Audio_Research.md", r"^\| A\d{2} \|", "audio")
    if (len(environment), len(character), len(audio)) != (56, 50, 60):
        raise ValueError(f"Unexpected source template counts: {len(environment)}, {len(character)}, {len(audio)}")

    rows = []
    for task in environment:
        raw_id = task["raw_id"]
        if raw_id in ENV_MAPS:
            for code, name in MAPS:
                add(rows, task, task_id=f"{raw_id}-{code}", variant=name, map_name=name,
                    suffix=f"Scope and evidence are for {name} only; another map's capture cannot satisfy this row.")
        elif raw_id in ENV_SURFACES:
            for code, name in SURFACES:
                add(rows, task, task_id=f"{raw_id}-{code}", variant=name, surface=name,
                    suffix=f"Scope this pass to {name}; identify representative assets and record a separate result for this surface.")
        else:
            add(rows, task)

    for task in character:
        raw_id = task["raw_id"]
        if raw_id in CHAR_WEAPONS:
            variants = RIFLES if raw_id == "C11" else WEAPONS
            for code, name in variants:
                add(rows, task, task_id=f"B61-{raw_id}-{code}", variant=name, weapon=name,
                    suffix=f"This row is for {name} only; retain a separate capture/result for that weapon.")
        else:
            add(rows, task, task_id=f"B61-{raw_id}")

    for task in audio:
        raw_id = task["raw_id"]
        if raw_id in AUDIO_WEAPONS:
            for code, name in WEAPONS:
                add(rows, task, task_id=f"B61-{raw_id}-{code}", variant=name, weapon=name,
                    suffix=f"This row is for {name} only; retain a separate capture/result for that weapon.")
        elif raw_id in AUDIO_MAPS:
            for code, name in MAPS:
                add(rows, task, task_id=f"B61-{raw_id}-{code}", variant=name, map_name=name,
                    suffix=f"This row covers {name} only; record the route and source positions used.")
        elif raw_id in AUDIO_SURFACES:
            for code, name in SURFACES:
                add(rows, task, task_id=f"B61-{raw_id}-{code}", variant=name, surface=name,
                    suffix=f"This row is for {name} only; record the surface/clip mapping separately.")
        else:
            add(rows, task, task_id=f"B61-{raw_id}")

    if len(rows) != 390 or len({row["id"] for row in rows}) != len(rows):
        raise ValueError(f"Leaf register must contain 390 unique tasks; got {len(rows)}")
    return rows


def main():
    rows = expand()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with OUTPUT.open("w", encoding="utf-8", newline="") as output:
        writer = csv.DictWriter(output, fieldnames=FIELDS)
        writer.writeheader()
        writer.writerows(rows)
    domains = Counter(row["domain"] for row in rows)
    owners = Counter(row["owner"] for row in rows)
    print(f"Wrote {len(rows)} unique tasks to {OUTPUT}")
    print("Domains:", dict(domains))
    print("Primary owners:", dict(owners))


if __name__ == "__main__":
    main()
