#!/usr/bin/env python3
"""量网关的前缀缓存 TTL：同一份 body 隔多久重发，cached_tokens 何时归零。

为什么不能用「换参数连发」测（2026-10-30 的教训）
---------------------------------------------------
旧版脚本依次发六组、每组加一个缓存参数，结论是「参数之间毫无差别」，**但那个结论是错的**：
每组换了 ``prompt_cache_key`` 的值（ttl-probe-0001…0005），每组都是**不同的前缀**，
本该命中却拿到 0；后三组拿到 753 只是碰巧共享了可命中的前缀，与参数无关。
更要命的是六次连发**几乎没有时间跨度**——TTL 是时间维度的事，零间隔测不出任何 TTL 语义。

真日志给出的答案（315 发、883 分钟跨度、同亲和值内相邻两发）
------------------------------------------------------------
    0–30 秒     90 样本  平均 79.4%
    30–120 秒  114 样本  平均 87.2%
    2–10 分钟   30 样本  平均 86.1%     ← 过了 5 分钟也不掉
    27 分钟              命中 98.5%
    65 分钟              命中 0%

即网关的 TTL 明显长于 OpenAI 官方默认（5–10 分钟）。当前缓存的瓶颈是**前缀分叉**
（append 88.2% vs break 35.3%），不是 TTL。

怎么正确地量
--------------
TTL 只有一个自变量：**时间**。所以：
  1. 同一份 body、**同一个** prompt_cache_key（不换！）
  2. 隔不同时间重发，看 cached_tokens 何时归零
  3. 每档时间发两次取较大值——网关有副本抖动，单发不可信
  4. 冷启动一次（换 key 或换 body）以确保起点是干净的

用法:
    CMD_API_KEY=xxx python3 src/scripts/probe/ttl-probe.py
    CMD_API_KEY=xxx python3 src/scripts/probe/ttl-probe.py --waits 30,120,360,720,1800
"""
import argparse
import json
import os
import time
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")
MODEL = os.environ.get("CMD_MODEL", "meta/muse-spark-1.3-contributor")
FILLER = "以下是需要处理的事项清单，请逐条阅读并理解上下文。" * 60
UA = {"Authorization": "Bearer " + (KEY or ""), "Content-Type": "application/json",
      "User-Agent": "uiharu-mind-probe/1.0"}


def post(body):
    req = urllib.request.Request(BASE + "/v1/chat/completions",
                                 data=json.dumps(body).encode(), headers=UA)
    try:
        with urllib.request.urlopen(req) as r:
            return json.load(r)
    except urllib.error.HTTPError as e:
        return {"error": e.read().decode("utf-8", "ignore")[:300], "status": e.code}


def cached_of(body):
    """发一次，返回 (prompt_tokens, cached_tokens)；失败返回 (None, None)"""
    r = post(body)
    if "error" in r:
        return None, None
    u = r.get("usage") or {}
    d = u.get("prompt_tokens_details") or {}
    return u.get("prompt_tokens"), d.get("cached_tokens", 0)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--waits", default="0,30,120,360,720,1800",
                    help="各档等待秒数（逗号分隔）")
    ap.add_argument("--key", default="ttl-probe-fixed",
                    help="prompt_cache_key，全程不变——换 key 就换了前缀，测不出 TTL")
    ap.add_argument("--tries", type=int, default=2, help="每档发几次取最大值（抗副本抖动）")
    args = ap.parse_args()
    if not KEY:
        print("需要 CMD_API_KEY 环境变量", flush=True)
        return 2

    waits = [int(x) for x in args.waits.split(",")]
    body = {"model": MODEL, "messages": [
        {"role": "system", "content": FILLER},
        {"role": "user", "content": "第一个问题。"}],
        "max_tokens": 32, "prompt_cache_key": args.key}

    # 冷启动：换一个 key 发一次，确保下面测的是「新前缀在时间推移后还能不能命中」
    warm = dict(body)
    warm["prompt_cache_key"] = args.key + "-warm"
    post(warm)
    print("冷启动完成（用了一个不同的 key）\n", flush=True)

    print(f"{'等待':>10}  {'prompt':>8}  {'cached':>8}  命中率")
    for wait in waits:
        if wait:
            time.sleep(wait)
        best_p, best_c = None, -1
        for _ in range(max(args.tries, 1)):
            p, c = cached_of(body)
            if p is None:
                print(f"{wait:>9}s  请求失败")
                break
            if c > best_c:
                best_p, best_c = p, c
        else:
            hit = best_c / best_p * 100 if best_p else 0
            print(f"{wait:>9}s  {best_p:>8}  {best_c:>8}  {hit:.1f}%", flush=True)

    print("\n读法：cached 掉到 0 的那一档就是 TTL 的量级。")
    print("单发不可信（网关按副本各存一份缓存），所以每档取最大值。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
