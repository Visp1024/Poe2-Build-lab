"""Маппинг «id ноды дерева PoB → PassiveSkills.Id» для экспорта .build
(внутриигровой Build Planner PoE2 0.5).

В tree.json у ноды есть только числовой `skill` (= PassiveSkills.PassiveSkillGraphId),
а игра в .build ждёт строковый идентификатор («energy_shield15»). Связь живёт
только в GGPK-таблице PassiveSkills, поэтому файл генерируется, а не пишется руками.

Регенерация (после обновления дерева или патча игры):
    cd PBLExport/ggpk_export && npx pathofexile-dat export --config config.json --output-dir tables
    python PBLExport/gen_build_planner_ids.py
"""
import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
TABLE = ROOT / "PBLExport" / "ggpk_export" / "tables" / "English" / "PassiveSkills.json"
OUT = ROOT / "PBLApp.Core" / "Export" / "passive_planner_ids.json"


def main() -> None:
    rows = json.loads(TABLE.read_text(encoding="utf-8"))
    by_graph_id = {
        r["PassiveSkillGraphId"]: r["Id"]
        for r in rows
        if r.get("Id") and r.get("PassiveSkillGraphId")
    }

    # Держим только ноды, которые реально встречаются в деревьях репозитория:
    # полная таблица вдвое больше и на 90% состоит из нод, которых в дереве нет.
    used: set[int] = set()
    for tree in sorted((ROOT / "src" / "TreeData").glob("*/tree.json")):
        nodes = json.loads(tree.read_text(encoding="utf-8")).get("nodes", {})
        for node in nodes.values():
            if isinstance(node, dict) and isinstance(node.get("skill"), int):
                used.add(node["skill"])

    mapping = {str(k): by_graph_id[k] for k in sorted(used) if k in by_graph_id}
    missing = sorted(used - set(by_graph_id))

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(mapping, ensure_ascii=False, indent=0, sort_keys=True),
                   encoding="utf-8")
    print(f"{len(mapping)} нод -> {OUT.relative_to(ROOT)}"
          f"{f', без Id в GGPK: {len(missing)} {missing[:10]}' if missing else ''}")


if __name__ == "__main__":
    main()
