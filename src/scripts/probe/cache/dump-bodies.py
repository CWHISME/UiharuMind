#!/usr/bin/env python3
"""逐条打印某个模型的请求体消息摘要,定位「前缀为什么对不上」。

用法:
  python3 src/scripts/probe/cache/dump-bodies.py --model <模型id>
  python3 src/scripts/probe/cache/dump-bodies.py --model <模型id> --full   # 连内容一起打
"""
import argparse
import importlib.util
import json
import os

_spec = importlib.util.spec_from_file_location(
    "cache_audit",
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "perf", "cache-audit.py"))
ca = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(ca)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--logs", default=os.path.expanduser("~/.uiharu/Logs"))
    ap.add_argument("--full", action="store_true")
    args = ap.parse_args()

    paths = ca.log_files(args.logs, "Bodies")
    bodies, stats = ca.parse_bodies(paths, args.model)
    print("共 %d 份请求体（%s）" % (len(bodies), args.model))

    for i, body in enumerate(bodies):
        print("\n===== #%d  %s  %d bytes  %d 条消息" %
              (i, body["file"], body["bytes"], len(body["messages"])))
        for j, msg in enumerate(body["messages"]):
            role = msg.get("role")
            keys = sorted(k for k in msg.keys() if k not in ("role", "content"))
            content = msg.get("content")
            if isinstance(content, list):
                kinds = ",".join(c.get("type", "?") for c in content)
                size = len(json.dumps(content, ensure_ascii=False))
            elif isinstance(content, str):
                kinds = "text"
                size = len(content)
            else:
                kinds = type(content).__name__
                size = len(json.dumps(content, ensure_ascii=False)) if content else 0
            extra = ""
            if "reasoning_content" in msg:
                rc = msg["reasoning_content"]
                extra += "  reasoning_content(%d)" % (len(rc) if isinstance(rc, str) else -1)
            if "reasoning_details" in msg:
                rd = msg["reasoning_details"]
                extra += "  reasoning_details(%d)" % (len(json.dumps(rd, ensure_ascii=False)))
            if "reasoning" in msg:
                extra += "  reasoning(%d)" % len(json.dumps(msg["reasoning"], ensure_ascii=False))
            print("  [%02d] %-9s %-14s %6d  其它键:%s%s" %
                  (j, role, kinds, size, ",".join(keys) if keys else "-", extra))
            if args.full:
                print("       " + json.dumps(content, ensure_ascii=False)[:600])


if __name__ == "__main__":
    main()
