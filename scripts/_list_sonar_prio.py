import json

data = json.load(open(r"E:\workspace\webfx-workspace\qolome-devtoy\.sonar-issues.json", encoding="utf-8"))
issues = data["all"]
prio = []
for i in issues:
    t = i.get("type")
    sev = i.get("severity")
    if t in ("BUG", "VULNERABILITY", "SECURITY_HOTSPOT") or sev in ("BLOCKER", "CRITICAL", "MAJOR"):
        prio.append(i)

print("priority count", len(prio))


def sort_key(x):
    order = {"BUG": 0, "VULNERABILITY": 1, "SECURITY_HOTSPOT": 2}.get(x["type"], 3)
    sev_order = {"BLOCKER": 0, "CRITICAL": 1, "MAJOR": 2, "MINOR": 3, "INFO": 4}.get(x.get("severity"), 9)
    return (order, sev_order, x.get("rule") or "", x.get("line") or 0)


for i in sorted(prio, key=sort_key):
    comp = i.get("component", "").split(":")[-1]
    msg = (i.get("message") or "")[:140]
    print(f"{i['type']:16} {i.get('severity'):8} {i.get('rule'):45} L{i.get('line')} {comp}")
    print(" ", msg)
