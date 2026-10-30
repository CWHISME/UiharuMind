#!/usr/bin/env python3
"""分辨 <模型id> 的缓存掉底是「字节变了」还是「路由换了副本」。

生产日志里同一个亲和值下命中是交替的(命中/不命中/命中/不命中),而那一段
请求体是纯字节追加、连 reasoning_details 都没有——所以更像是路由到不同副本。
但要证死它,得看同一份 body 连发很多次时的命中形态:

  若前几次 miss 之后全 hit   → 冷启动 + 路由稳定,生产里的交替另有原因
  若一直随机 miss/hit 交替   → 亲和没起作用,请求被轮询到多个副本
  若一直全 hit               → 路由稳,生产里的问题在字节侧

三组各发 N 次,body 完全相同,只有亲和的给法不同:
  P1  x-session-id 头 + prompt_cache_key body
  P2  两者都不给
  P3  只给 x-session-id 头

同时记 system_fingerprint:它每次都变说明请求没落在同一个后端。

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-routing.py [--rounds 10]
"""
import argparse
import json
import os
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")
MODEL = os.environ.get("CMD_MODEL", "")
FILLER = "以下是需要处理的事项清单，请逐条阅读并理解上下文。" * 60
UA = {"Authorization": "Bearer " + KEY, "Content-Type": "application/json",
      "User-Agent": "uiharu-mind-probe/1.0"}


def post(body, affinity=None, body_key=None):
    headers = dict(UA)
    if affinity:
        headers["x-session-id"] = affinity
    payload = dict(body)
    if body_key:
        payload["prompt_cache_key"] = body_key
    request = urllib.request.Request(BASE + "/v1/chat/completions",
                                     data=json.dumps(payload).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:300], "status": error.code}


BODY = {"model": MODEL, "max_tokens": 16, "messages": [
    {"role": "system", "content": FILLER},
    {"role": "user", "content": "第一个问题:请记住这段前缀。"}]}


def run(label, rounds, affinity=None, body_key=None):
    seq = []
    fps = set()
    for _ in range(rounds):
        r = post(BODY, affinity, body_key)
        if "error" in r:
            print("  失败:", r)
            return
        u = r.get("usage") or {}
        d = u.get("prompt_tokens_details") or {}
        seq.append(d.get("cached_tokens", 0))
        fps.add(r.get("system_fingerprint"))
    hits = sum(1 for v in seq if v > 200)
    print("  cached 序列: %s" % " ".join("%s" % v for v in seq))
    print("  命中 %d/%d,fingerprint %d 个不同" % (hits, rounds, len(fps)))
    return seq


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--rounds", type=int, default=10)
    args = ap.parse_args()

    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1
    if not MODEL:
        print("缺少 CMD_MODEL（要测的模型 id）")
        return 1

    print("--- P1 头 + body 都给亲和 ---")
    run("P1", args.rounds, "route-P1-11111111", "route-P1-11111111")
    print("--- P2 都不给 ---")
    run("P2", args.rounds)
    print("--- P3 只给头 ---")
    run("P3", args.rounds, "route-P3-33333333")
    return 0


if __name__ == "__main__":
    main()
