#!/usr/bin/env python3
"""问清三个问题:票据到底怎么影响缓存。

前置事实(已由 cache-ab.py 验证):同一份 body 连发两次,第二次能命中——
前缀缓存机制本身没问题,网关路由也稳。

这里测三组,每组连发 4 发,前缀逐步追加(模拟真实会话):

  D  每条 assistant 都带**稳定**的票据(票据随消息持久重放,内容不变)
  E  完全不带票据
  F  每一发都换一张新票据(模拟当前实现:票据挂在最后一条 assistant 上,
     上一发的那条下一发就没了 → 每发字节都在变)

判读:
  - D 的 cached 序列 ≈ E → 稳定重放票据不伤缓存,那就该做持久化重放
  - D 的 cached 序列 ≪ E → 票据本身伤缓存(上游对含 reasoning_details 的前缀不建缓存),
     那正确做法是**不**回填票据
  - F 的 cached 全掉 → 证实当前"一次即消费"的实现是自伤

顺带记 reasoning_tokens:带票据是否真的省了推理。
用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-ab2.py
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
KEY = os.environ.get("CMD_API_KEY")
MODEL = os.environ.get("CMD_MODEL", "")

FILLER = "以下是需要处理的事项清单，请逐条阅读并理解上下文。" * 60
UA = {"Authorization": "Bearer " + KEY, "Content-Type": "application/json",
      "User-Agent": "uiharu-mind-probe/1.0"}


def post(body, cache_key):
    headers = dict(UA)
    if cache_key:
        headers["x-session-id"] = cache_key
    request = urllib.request.Request(BASE + "/v1/chat/completions",
                                     data=json.dumps(body).encode(), headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:400], "status": error.code}


def stats(response):
    u = response.get("usage") or {}
    d = u.get("prompt_tokens_details") or {}
    c = u.get("completion_tokens_details") or {}
    return u.get("prompt_tokens"), d.get("cached_tokens"), c.get("reasoning_tokens")


def take_ticket():
    """先跑一轮,拿到真票据(格式照 CommandCode 实测)"""
    body = {"model": MODEL, "messages": [
        {"role": "system", "content": FILLER},
        {"role": "user", "content": "算一下 17*23,先想清楚再回答。"}], "max_tokens": 256}
    r = post(body, "probe-ticket-0000")
    text = json.dumps(r, ensure_ascii=False)
    if "reasoning.encrypted" not in text:
        print("拿不到票据,响应片段:", text[:300])
        return None
    for choice in r.get("choices") or []:
        msg = choice.get("message") or {}
        details = msg.get("reasoning_details")
        if details:
            return details
    return None


def run(label, mode, ticket, cache_key, rounds=4):
    """mode: stable=每条 assistant 带同一张票 / none=不带 / fresh=每发换新票"""
    print("\n--- %s ---" % label)
    messages = [
        {"role": "system", "content": FILLER},
        {"role": "user", "content": "第一个问题:请记住这段前缀。"},
    ]
    results = []
    for i in range(rounds):
        assistant = {"role": "assistant", "content": "第%d 步的回复,长度固定。" % i}
        if mode == "stable":
            assistant["reasoning_details"] = ticket
        elif mode == "fresh":
            # 每发给上一条 assistant 挂一张"新的"票 → 历史那条的字节每发都变
            for m in messages:
                if m.get("role") == "assistant" and "reasoning_details" in m:
                    m["reasoning_details"] = [{"type": "reasoning.encrypted",
                                                "data": ticket[0]["data"][:-4] + "%04d" % i,
                                                "index": 0}]
        messages.append(assistant)
        messages.append({"role": "user", "content": "第%d 个问题:继续。" % i})
        body = {"model": MODEL, "messages": messages, "max_tokens": 32}
        r = post(body, cache_key)
        if "error" in r:
            print("  第%d发 失败:%s" % (i + 1, r))
            break
        prompt, cached, reasoning = stats(r)
        print("  第%d发: prompt=%-6s cached=%-6s reasoning_tokens=%s" % (i + 1, prompt, cached, reasoning))
        results.append((prompt, cached))
    return results


def main():
    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1
    if not MODEL:
        print("缺少 CMD_MODEL（要测的模型 id）")
        return 1
    ticket = take_ticket()
    if not ticket:
        print("没拿到票据,后面几组无法测")
        return 1
    print("拿到票据,长度 %d 字符" % len(json.dumps(ticket, ensure_ascii=False)))

    run("D 每条 assistant 带稳定票据", "stable", ticket, "probe-DDDD4444")
    run("E 完全不带票据", "none", ticket, "probe-EEEE5555")
    run("F 每发换新票(当前实现的效果)", "fresh", ticket, "probe-FFFF6666")
    return 0


if __name__ == "__main__":
    sys.exit(main())
