#!/usr/bin/env python3
"""决定性实验：把日志里的真实请求体原样重放,看命中形态能不能复现。

前面的对照实验(cache-ab2 / cache-routing)证明:同一份 body 连发,10/10 全命中。
所以机制与路由都没问题。剩下的可能只有两个:
  (a) 那些 body 本身带着让缓存不成立的性质 → 重放会复现「命中/不命中」交替
  (b) 当时的环境里有别的因素(并发、TTL、上游换副本)→ 重放会全命中

两个都重放:
  按原序重放 Bodies.txt 里 <模型id> 的 #11..#14
  每个 body 单独连发 3 次
判读:
  原序重放出现 113/命中交替 → (a),问题在字节
  全命中 → (b),问题在环境(并发或 TTL),与我们的 body 无关

用法:
  python3 src/scripts/probe/cache/replay-bodies.py [--logs ~/.uiharu/Logs]
"""
import argparse
import importlib.util
import json
import os
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")
UA = {"Authorization": "Bearer " + KEY, "Content-Type": "application/json",
      "User-Agent": "uiharu-mind-probe/1.0"}


def load_cache_audit(logs):
    here = os.path.dirname(os.path.abspath(__file__))
    spec = importlib.util.spec_from_file_location(
        "ca", os.path.join(here, "..", "..", "perf", "cache-audit.py"))
    ca = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ca)
    return ca


def raw_bodies(ca, logs, model_filter):
    """按日志顺序切出原始 body(含顶层字段),不做任何规范化"""
    out = []
    for path in ca.log_files(logs, "Bodies"):
        raw = ca.read_text(path)
        if model_filter and model_filter not in raw:
            continue
        fixed = ca.repair(raw)
        marks = list(ca.HEADER_RE.finditer(fixed))
        for i, m in enumerate(marks):
            end = marks[i + 1].start() if i + 1 < len(marks) else len(fixed)
            block = fixed[m.end():end].strip()
            try:
                obj, _c = json.JSONDecoder().raw_decode(block)
            except json.JSONDecodeError:
                continue
            if not isinstance(obj, dict) or "messages" not in obj:
                continue
            if model_filter and model_filter not in obj.get("model", ""):
                continue
            out.append((os.path.basename(path), obj))
    return out


def post(body, affinity=None):
    headers = dict(UA)
    if affinity:
        headers["x-session-id"] = affinity
    request = urllib.request.Request(BASE + "/v1/chat/completions",
                                     data=json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode("utf-8", "replace")
            if not raw.strip():
                return {"error": "空响应", "status": response.status}
            try:
                return json.loads(raw)
            except json.JSONDecodeError:
                # body 里带 stream:true 时响应是 SSE,usage 在收尾那一帧
                for line in raw.splitlines():
                    if line.startswith("data: ") and '"usage"' in line:
                        try:
                            return json.loads(line[6:])
                        except json.JSONDecodeError:
                            continue
                return {"error": raw[:200], "status": response.status}
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:200], "status": error.code}


def cached_of(response):
    if "error" in response:
        return "ERR:" + str(response.get("status"))
    u = response.get("usage") or {}
    d = u.get("prompt_tokens_details") or {}
    return d.get("cached_tokens", 0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--logs", default=os.path.expanduser("~/.uiharu/Logs"))
    ap.add_argument("--model", default="")
    ap.add_argument("--repeat", type=int, default=3)
    args = ap.parse_args()

    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1

    ca = load_cache_audit(args.logs)
    bodies = raw_bodies(ca, args.logs, args.model)
    if not bodies:
        print("没切到 body")
        return 1
    print("切到 %d 份 body" % len(bodies))

    # 只取最新那一份日志文件的 body —— 那段是用户刚跑出来的
    newest = bodies[-1][0]
    recent = [b for f, b in bodies if f == newest]
    print("最新文件 %s 里 %d 份\n" % (newest, len(recent)))

    print("=== 一、按原序重放(每份一次) ===")
    for i, body in enumerate(recent):
        print("  #%d cached=%s" % (i, cached_of(post(body))))

    print("\n=== 二、每份连发 %d 次 ===" % args.repeat)
    for i, body in enumerate(recent):
        seq = [cached_of(post(body)) for _ in range(args.repeat)]
        print("  #%d %s" % (i, seq))

    print("\n=== 三、同一份 body 连发 %d 次(看上游对这份前缀稳不稳) ===" % args.repeat)
    body = recent[0]
    seq = [cached_of(post(body)) for _ in range(args.repeat)]
    print("  序列: %s" % seq)
    hits = sum(1 for v in seq if isinstance(v, int) and v > 200)
    print("  命中 %d/%d" % (hits, len(seq)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
