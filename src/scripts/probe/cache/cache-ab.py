#!/usr/bin/env python3
"""对照实验：前缀缓存到底是被「字节变了」打掉的，还是被「路由」打掉的。

三组对照，每组连发两次，字节完全相同：
  A  原样重发同一份 body（含 reasoning_details）
  B  同一份 body，但把 assistant 上的 reasoning_details 去掉
  C  同一份 body，去掉 reasoning_details，且换一个 prompt_cache_key

判读：
  - A 第二次命中 → 前缀缓存机制本身没问题，路由也稳
  - A 第二次不命中 → 与我们的字节无关，是上游/路由的事
  - B 第二次命中、A 不命中 → 是 reasoning_details 这个键把前缀打了
  - B 第二次也不命中 → 同一份 body 重发都不命中，缓存根本不看这个前缀

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-ab.py [--model <模型id>]
"""
import argparse
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")

# 一段够长的前缀：缓存一般有最小长度门槛，太短不会命中
FILLER = "以下是需要处理的事项清单，请逐条阅读并理解上下文。" * 60


def post(body, cache_key=None, affinity=None):
    headers = {
        "Authorization": "Bearer " + KEY,
        "Content-Type": "application/json",
        "User-Agent": "uiharu-mind-probe/1.0",
    }
    if cache_key:
        headers["x-session-id"] = cache_key
    request = urllib.request.Request(
        BASE + "/v1/chat/completions",
        data=json.dumps(body).encode(),
        headers=headers,
    )
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:400], "status": error.code}


def usage_of(response):
    u = response.get("usage") or {}
    d = u.get("prompt_tokens_details") or {}
    return u.get("prompt_tokens"), d.get("cached_tokens"), response.get("system_fingerprint")


def build(model, with_details, ticket):
    messages = [
        {"role": "system", "content": FILLER},
        {"role": "user", "content": "第一个问题：请记住这段前缀。"},
    ]
    assistant = {"role": "assistant", "content": "好的，我记住了。"}
    if with_details:
        assistant["reasoning_details"] = [ticket]
    messages.append(assistant)
    messages.append({"role": "user", "content": "第二个问题：继续。"})
    return {"model": model, "messages": messages, "max_tokens": 64}


def run_group(label, model, body, cache_key):
    print("\n--- %s ---" % label)
    for i in (1, 2):
        response = post(body, cache_key)
        if "error" in response:
            print("  第%d次: 失败 %s" % (i, response))
            return
        prompt, cached, fp = usage_of(response)
        print("  第%d次: prompt=%s cached=%s fp=%s" % (i, prompt, cached, fp))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="")
    args = ap.parse_args()

    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1

    ticket = {"type": "reasoning.encrypted", "data": "Q-PaD" + "x" * 200,
              "id": "rs_probe:rs_probe", "format": "probe-v1", "index": 0}

    # A：带票据，同一个 cache_key，连发两次
    run_group("A 带票据、同 key 重发", args.model, build(args.model, True, ticket),
              "probe-AAAA1111")

    # B：不带票据，同一个 cache_key，连发两次
    run_group("B 不带票据、同 key 重发", args.model, build(args.model, False, ticket),
              "probe-BBBB2222")

    # C：不带票据，换 key，连发两次（key 变了但字节一样）
    run_group("C 不带票据、换 key 重发", args.model, build(args.model, False, ticket),
              "probe-CCCC3333")

    return 0


if __name__ == "__main__":
    sys.exit(main())
