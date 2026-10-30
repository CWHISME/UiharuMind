#!/usr/bin/env python3
"""测上游抖动的性质:同一份 body 连发,命中形态是什么样的。

已知事实(replay-bodies.py 实测):同一份 body 连发 12 次,命中 7/12,
字节完全相同——所以抖动来自上游,不是我们的请求。

这一支再测三件事:
  1. 抖动与并发有关吗 → 同一份 body 连发 vs 间隔并发发,命中率是否不同
  2. 抖动是周期性的吗 → 记录足够长的序列,看是否交替/周期
  3. 换 prompt_cache_key 会不会换一份缓存 → 同前缀不同 key 的命中对比

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-jitter.py [--rounds 16]
"""
import argparse
import json
import os
import threading
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")
MODEL = os.environ.get("CMD_MODEL", "")
FILLER = "以下是需要处理的事项清单，请逐条阅读并理解上下文。" * 60
UA = {"Authorization": "Bearer " + KEY, "Content-Type": "application/json",
      "User-Agent": "uiharu-mind-probe/1.0"}


def post(body, cache_key=None):
    headers = dict(UA)
    if cache_key:
        headers["x-session-id"] = cache_key
    request = urllib.request.Request(BASE + "/v1/chat/completions",
                                     data=json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode("utf-8", "replace")
            # SSE:usage 在收尾帧;一次性 JSON:usage 在顶层
            for line in raw.splitlines():
                if line.startswith("data: ") and '"usage"' in line:
                    try:
                        obj = json.loads(line[6:])
                    except json.JSONDecodeError:
                        continue
                    u = obj.get("usage") or {}
                    d = u.get("prompt_tokens_details") or {}
                    return d.get("cached_tokens", 0)
            try:
                obj = json.loads(raw)
                u = obj.get("usage") or {}
                d = u.get("prompt_tokens_details") or {}
                return d.get("cached_tokens", 0)
            except (json.JSONDecodeError, AttributeError):
                return -1
    except urllib.error.HTTPError as error:
        return -2


BODY = {"model": MODEL, "max_tokens": 16, "messages": [
    {"role": "system", "content": FILLER},
    {"role": "user", "content": "第一个问题:请记住这段前缀。"}]}


def hit(v):
    return "H" if isinstance(v, int) and v > 200 else ("." if v == 0 else "x")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=16)
    args = ap.parse_args()
    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1
    if not MODEL:
        print("缺少 CMD_MODEL（要测的模型 id）")
        return 1

    print("=== 1. 串行连发 %d 次 ===" % args.rounds)
    seq = [post(BODY) for _ in range(args.rounds)]
    print("  序列: %s" % " ".join("%s(%s)" % (hit(v), v) for v in seq))
    print("  命中 %d/%d" % (sum(1 for v in seq if hit(v) == "H"), args.rounds))

    print("\n=== 2. 四路并发,每路 %d 次(看并发是否加剧抖动) ===" % args.rounds)
    results = [[] for _ in range(4)]

    def worker(i):
        for _ in range(args.rounds):
            results[i].append(post(BODY))

    threads = [threading.Thread(target=worker, args=(i,)) for i in range(4)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    total = 0
    for i, r in enumerate(results):
        h = sum(1 for v in r if hit(v) == "H")
        total += h
        print("  路%d: %s  命中 %d/%d" % (i, " ".join(hit(v) for v in r), h, len(r)))
    print("  合计命中 %d/%d" % (total, 4 * args.rounds))

    print("\n=== 3. 同前缀、不同 prompt_cache_key ===")
    for tag, key in (("keyA", "jit-A-11111"), ("keyB", "jit-B-22222"), ("keyA 再来", "jit-A-11111")):
        seq = [post(BODY, key) for _ in range(4)]
        print("  %-10s %s" % (tag, " ".join("%s(%s)" % (hit(v), v) for v in seq)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
