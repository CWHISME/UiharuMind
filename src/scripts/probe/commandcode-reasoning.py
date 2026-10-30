#!/usr/bin/env python3
"""量一件事：这个模型在 CommandCode 上到底有没有「思考票据」，以及它长什么样。

为什么需要专门量：加密思考不进界面，我们的适配层又只认 reasoning_content
（reasoning_details 在 Microsoft.Extensions.AI.OpenAI 里根本没被读），于是
「服务端发了、我们丢了」与「服务端根本没发」在应用里长得一模一样，
只有 usage 里那个 reasoning_tokens 在动。muse-spark 正是这种。

四步，依次回答四个问题：

  1. 它声明了哪些端点（/responses 在不在）
  2. /chat/completions 的响应里有没有 reasoning_content / reasoning_details（含加密条目）
  3. /responses + include:["reasoning.encrypted_content"] 下不下发 encrypted_content
  4. 把第一轮的 output 原样塞回第二轮，能不能过（回填的全部内容就在这一步）

用法:
    CMD_API_KEY=sk-... python3 src/scripts/probe/commandcode-reasoning.py
    CMD_API_KEY=sk-... python3 src/scripts/probe/commandcode-reasoning.py meta/muse-spark-1.3-contributor

只发三四次最小请求；密钥只在本进程的环境变量里，不落盘、不写日志。
"""
import json
import os
import sys
import urllib.error
import urllib.request

BASE = os.environ.get("CMD_BASE", "https://api.commandcode.ai/provider")
MODEL = sys.argv[1] if len(sys.argv) > 1 else "meta/muse-spark-1.3-contributor"
KEY = os.environ.get("CMD_API_KEY")
PROMPT = "算一下 17*23，先想清楚再回答，最后只给结论。"


def post(path, body):
    request = urllib.request.Request(
        BASE + path,
        data=json.dumps(body).encode(),
        headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def get(path):
    request = urllib.request.Request(BASE + path, headers={"Authorization": "Bearer " + KEY})
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def show_error(result):
    print("  请求失败:", result["error"])
    return True


def step_endpoints():
    print(f"== 1. {MODEL} 声明的端点 ==")
    listing = get("/v1/models")
    if "error" in listing:
        return show_error(listing)
    items = listing.get("data") if isinstance(listing, dict) else listing
    if not isinstance(items, list):
        print("  目录返回的不是列表，跳过这项检查")
        return True
    for item in items:
        if MODEL in (item.get("id") or ""):
            print("  endpoints:", item.get("supported_endpoints") or "(未声明)")
            return False
    print("  目录里没找到这个 id：检查拼写，或这个账户/区域没有这个模型")
    return True


def step_chat():
    print("\n== 2. /chat/completions 的响应里有什么 ==")
    result = post("/v1/chat/completions",
                  {"model": MODEL, "messages": [{"role": "user", "content": PROMPT}], "stream": False})
    if "error" in result:
        return show_error(result)

    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    extra = [k for k in message if k not in ("role", "content")]
    print("  message 字段:", extra or "(只有 role/content)")
    for field in ("reasoning_content", "reasoning", "reasoning_text", "reasoning_details"):
        if field in message:
            value = message[field]
            print(f"  {field}: {len(value)} {'条' if isinstance(value, list) else '字符'}")
    usage = result.get("usage") or {}
    print("  reasoning_tokens:", (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"))
    print("  content 长度:", len(message.get("content") or ""))
    return False


def step_responses():
    print("\n== 3. /responses + include:[\"reasoning.encrypted_content\"] ==")
    body = {
        "model": MODEL,
        "input": [{"role": "user", "content": [{"type": "input_text", "text": PROMPT}]}],
        "store": False,
        "include": ["reasoning.encrypted_content"],
    }
    result = post("/v1/responses", body)
    if "error" in result:
        return None, show_error(result)

    items = result.get("output") or []
    print("  output 条目:", [item.get("type") for item in items] or "(空)")
    encrypted = [item for item in items if item.get("encrypted_content")]
    if encrypted:
        print("  带 encrypted_content 的条目:", len(encrypted),
              "，首条长度", len(encrypted[0]["encrypted_content"]))
    else:
        print("  带 encrypted_content 的条目: 0 —— 这条路上也没有货，客户端到此为止")
    print("  reasoning 摘要条目:", len([i for i in items if i.get("type") == "reasoning"]))
    usage = result.get("usage") or {}
    print("  output_tokens_details:", usage.get("output_tokens_details"))
    return result, False


def step_replay(first):
    print("\n== 4. 第一轮的 output 原样塞回第二轮（票据能不能过） ==")
    items = list(first.get("output") or [])
    if not items:
        print("  第一轮没有 output，跳过")
        return
    items.append({"role": "user", "content": [{"type": "input_text", "text": "刚才那个结果再加 1 是多少？"}]})
    result = post("/v1/responses", {"model": MODEL, "input": items, "store": False,
                                    "include": ["reasoning.encrypted_content"]})
    if "error" in result:
        print("  第二轮被拒:", result.get("status"), result["error"][:400])
        return
    print("  第二轮通过，output 条目:", [item.get("type") for item in result.get("output") or []])
    usage = result.get("usage") or {}
    print("  input_tokens_details:", usage.get("input_tokens_details"))


def main():
    if not KEY:
        print("需要 CMD_API_KEY 环境变量（Studio → API keys 里那个）", file=sys.stderr)
        return 2

    # 不短路：端点查不到也照样量一次 chat，答案在响应里而不在目录里
    failed = step_endpoints()
    failed = step_chat() or failed
    first, failed_responses = step_responses()
    if first is not None:
        step_replay(first)
    if failed or failed_responses:
        return 1
    print("\n结论怎么看：第 3 步有 encrypted_content 且第 4 步通过 → 加密票据可回填，"
          "缺的只是 /responses 支路；第 3 步为 0 → 这个模型在网关的两条路上都不给货，"
          "别在客户端继续找办法。")
    return 0


if __name__ == "__main__":
    sys.exit(main())
