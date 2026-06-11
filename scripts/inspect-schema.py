"""Print column names for given tables in pathofexile-dat schema."""
import json, sys, os

SCHEMA = os.path.expandvars(r"%TEMP%\schema.min.json")
with open(SCHEMA, encoding="utf-8") as f:
    schema = json.load(f)

names = sys.argv[1:] or ["CostTypes"]
for name in names:
    entries = [t for t in schema["tables"] if t["name"] == name]
    for e in entries:
        print(f"-- {name} (validFor={e.get('validFor')}) --")
        for c in e["columns"]:
            cname = c.get("name") or "<unnamed>"
            ctype = c.get("type")
            ref   = c.get("references", {})
            ref_tbl = ref.get("table") if ref else None
            arr = "[]" if c.get("array") else ""
            ref_str = f" -> {ref_tbl}" if ref_tbl else ""
            print(f"  {cname}: {ctype}{arr}{ref_str}")
        print()
