#!/usr/bin/env python3
"""Responses 端点能不能治「前缀缓存命中抖动」。

已知(replay-bodies.py / cache-jitter.py 实测):
  同一份 body 连发 12 次,命中 7/12;四路并发时降到 69%。
  → 字节完全相同也掉命中,抖动来自上游,不是我们的请求。

这里对比两条路在同样条件下谁更稳:
  chat     /v1/chat/completions        usage.prompt_tokens_details.cached_tokens
  responses /v1/responses              usage.input_tokens_details.cached_tokens

每条路连发 N 次,统计命中率;再跑一轮四路并发。
Responses 侧试三种缓存参数组合(store:false 是硬要求,store:true 会在上游留状态)。

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/responses-cache.py [--rounds 12]
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


def post(path, body):
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(),
                                     headers=UA)
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as error:
        return -2
    obj = None
    for line in raw.splitlines():
        if line.startswith("data: ") and '"usage"' in line:
            try:
                obj = json.loads(line[6:])
            except json.JSONDecodeError:
                continue
    if obj is None:
        try:
            obj = json.loads(raw)
        except json.JSONDecodeError:
            return -1
    u = obj.get("usage") or {}
    d = u.get("input_tokens_details") or u.get("prompt_tokens_details") or {}
    return d.get("cached_tokens", 0)


CHAT_BODY = {"model": MODEL, "max_tokens": 16, "messages": [
    {"role": "system", "content": FILLER},
    {"role": "user", "content": "第一个问题:请记住这段前缀。"}]}

RESP_BODY = {"model": MODEL, "max_output_tokens": 16, "store": False, "input": [
    {"role": "system", "content": [{"type": "input_text", "text": FILLER}]},
    {"role": "user", "content": [{"type": "input_text", "text": "第一个问题:请记住这段前缀。"}]}]}


def hit(v):
    return "H" if isinstance(v, int) and v > 200 else ("." if v == 0 else "x")


def series(label, path, body, rounds):
    seq = [post(path, body) for _ in range(rounds)]
    h = sum(1 for v in seq if hit(v) == "H")
    print("  %-34s %s" % (label, " ".join("%s(%s)" % (hit(v), v) for v in seq)))
    print("  %-34s 命中 %d/%d" % ("", h, rounds))
    return h, rounds


def concurrent(label, path, body, rounds, lanes=4):
    results = [[] for _ in range(lanes)]

    def worker(i):
        for _ in range(rounds):
            results[i].append(post(path, body))

    threads = [threading.Thread(target=worker, args=(i,)) for i in range(lanes)]
    for t in threads:
        t.start()
    for t in threads:
        t.join()
    total = sum(sum(1 for v in r if hit(v) == "H") for r in results)
    print("  %-34s 命中 %d/%d" % (label, total, lanes * rounds))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=12)
    args = ap.parse_args()
    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1
    if not MODEL:
        print("缺少 CMD_MODEL（要测的模型 id）")
        return 1

    print("=== 串行 %d 次 ===" % args.rounds)
    series("chat/completions", "/v1/chat/completions", CHAT_BODY, args.rounds)
    series("responses (store:false)", "/v1/responses", RESP_BODY, args.rounds)

    body = dict(RESP_BODY)
    body["prompt_cache_key"] = "resp-probe-0001"
    series("responses + prompt_cache_key", "/v1/responses", body, args.rounds)

    body2 = dict(RESP_BODY)
    body2["include"] = ["reasoning.encrypted_content"]
    series("responses + include", "/v1/responses", body2, args.rounds)

    print("\n=== 四路并发,每路 %d 次 ===" % args.rounds)
    concurrent("chat/completions", "/v1/chat/completions", CHAT_BODY, args.rounds)
    concurrent("responses", "/v1/responses", RESP_BODY, args.rounds)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
