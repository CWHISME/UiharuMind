#!/usr/bin/env python3
"""量一件事：这个模型在 CommandCode 上到底有没有「思考票据」，以及它长什么样。

为什么需要专门量：加密思考不进界面，我们的适配层又只认 reasoning_content
（reasoning_details 在 Microsoft.Extensions.AI.OpenAI 里根本没被读），于是
「服务端发了、我们丢了」与「服务端根本没发」在应用里长得一模一样，
只有 usage 里那个 reasoning_tokens 在动。muse-spark 正是这种。

五步，依次回答五个问题：

  1. 它声明了哪些端点（/responses 在不在）
  2. /chat/completions 的响应里有没有 reasoning_content / reasoning_details
  3. 把这些字段原样塞回下一轮 chat，网关收不收（chat 侧回填的可行性）
  4. /responses + include:["reasoning.encrypted_content"] 下不下发 encrypted_content
  5. 把第一轮的 output 原样塞回第二轮 responses，能不能过

用法:
    CMD_API_KEY=sk-... python3 src/scripts/probe/commandcode-reasoning.py
    CMD_API_KEY=sk-... python3 src/scripts/probe/commandcode-reasoning.py meta/muse-spark-1.3-contributor

只发五六次最小请求；密钥只在本进程的环境变量里，不落盘、不写日志。
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
FOLLOW_UP = "刚才那个结果再加 1 是多少？"

# 缺省的 urllib UA 会被 Cloudflare 挡成 error code: 1010（按浏览器指纹拒），写成普通客户端的
HEADERS = {
    "Content-Type": "application/json",
    "Authorization": "Bearer " + (KEY or ""),
    "User-Agent": "uiharumind-probe/1.0",
    "Accept": "application/json",
}


def post(path, body):
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(), headers=HEADERS)
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # 成功响应自己也可能带 "error": null（Responses 就是这样），所以失败用一个专属键标出来
        return {"__http_error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def get(path):
    request = urllib.request.Request(BASE + path, headers=HEADERS)
    try:
        with urllib.request.urlopen(request) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        return {"__http_error": error.read().decode("utf-8", "ignore")[:600], "status": error.code}


def failed(result):
    if "__http_error" not in result:
        return False
    print("  请求失败:", result.get("status"), result["__http_error"].strip()[:400])
    return True


def snippet(value, limit=120):
    text = value if isinstance(value, str) else json.dumps(value, ensure_ascii=False)
    return text if len(text) <= limit else text[:limit] + f"…({len(text)} 字符)"


def step_endpoints():
    print(f"== 1. {MODEL} 声明的端点 ==")
    listing = get("/v1/models")
    if failed(listing):
        return True
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
    if failed(result):
        return None, True

    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    extra = [k for k in message if k not in ("role", "content")]
    print("  message 字段:", extra or "(只有 role/content)")
    for field in ("reasoning_content", "reasoning", "reasoning_text", "reasoning_details"):
        if field not in message:
            continue
        value = message[field]
        print(f"  {field}: {len(value)} {'条' if isinstance(value, list) else '字符'}")
        head = value[0] if isinstance(value, list) and value else value
        if isinstance(head, dict):
            print("    首条键:", list(head), "| type:", head.get("type"))
            for key, item in head.items():
                if key == "type":
                    continue
                print(f"    {key}: {snippet(item)}")
        else:
            print("    ", snippet(head))
    usage = result.get("usage") or {}
    print("  reasoning_tokens:", (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"))
    print("  cached_tokens:", (usage.get("prompt_tokens_details") or {}).get("cached_tokens"))
    print("  content:", snippet(message.get("content") or "", 60))
    return {"ticket_fields": {k: message[k] for k in extra if k != "content"}}, False


def step_chat_replay(chat):
    print("\n== 3. 把 chat 的票据原样塞回下一轮（网关收不收） ==")
    if not chat or not chat["ticket_fields"]:
        print("  第 2 步没有票据，跳过")
        return True
    assistant = {"role": "assistant", "content": "391"}
    assistant.update(chat["ticket_fields"])
    result = post("/v1/chat/completions", {
        "model": MODEL,
        "messages": [
            {"role": "user", "content": PROMPT},
            assistant,
            {"role": "user", "content": FOLLOW_UP},
        ],
        "stream": False,
    })
    if failed(result):
        print("  → 网关拒收原样票据：chat 侧没有回填这条路")
        return True

    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    usage = result.get("usage") or {}
    print("  → 收下了。回复:", snippet(message.get("content") or "", 60))
    print("  reasoning_tokens:", (usage.get("completion_tokens_details") or {}).get("reasoning_tokens"),
          " cached_tokens:", (usage.get("prompt_tokens_details") or {}).get("cached_tokens"))
    return False


def step_chat_stream():
    print("\n== 2b. 流式 delta 里有没有（应用走的是这条） ==")
    request = urllib.request.Request(
        BASE + "/v1/chat/completions",
        data=json.dumps({"model": MODEL, "messages": [{"role": "user", "content": PROMPT}],
                         "stream": True, "stream_options": {"include_usage": True}}).encode(),
        headers=HEADERS)
    try:
        with urllib.request.urlopen(request) as response:
            frames = 0
            with_details = 0
            shape = None
            for raw in response:
                line = raw.decode("utf-8", "ignore").strip()
                if not line.startswith("data:"):
                    continue
                payload = line[5:].strip()
                if payload == "[DONE]":
                    continue
                frames += 1
                delta = ((json.loads(payload).get("choices") or [{}])[0].get("delta")) or {}
                if "reasoning_details" in delta:
                    with_details += 1
                    shape = shape or delta["reasoning_details"]
    except urllib.error.HTTPError as error:
        print("  请求失败:", error.code, error.read().decode("utf-8", "ignore")[:300])
        return True

    print(f"  data 帧 {frames} 个，其中带 reasoning_details 的 {with_details} 个")
    if shape:
        head = shape[0] if isinstance(shape, list) and shape else shape
        print("  首帧形状:", list(head) if isinstance(head, dict) else type(head).__name__,
              "| type:", head.get("type") if isinstance(head, dict) else None,
              "| data 长度:", len(head.get("data") or "") if isinstance(head, dict) else None)
    else:
        print("  → 流式里没有：要么网关只在非流式下发，要么它走别的事件名")
    return with_details == 0


def step_foreign_replay(chat):
    print("\n== 3b. 把 Meta 的票据发给另一个模型（网关校验不校验） ==")
    if not chat or not chat["ticket_fields"]:
        print("  第 2 步没有票据，跳过")
        return True
    other = "deepseek/deepseek-v4-flash"
    assistant = {"role": "assistant", "content": "391"}
    assistant.update(chat["ticket_fields"])
    result = post("/v1/chat/completions", {
        "model": other,
        "messages": [{"role": "user", "content": PROMPT}, assistant, {"role": "user", "content": FOLLOW_UP}],
        "stream": False,
    })
    if failed(result):
        print(f"  → 换到 {other} 被拒：网关按模型校验票据，客户端必须做同模型门禁")
        return False
    message = ((result.get("choices") or [{}])[0].get("message")) or {}
    print(f"  → {other} 也收下了（是不是真用上不知道）。回复:", snippet(message.get("content") or "", 40))
    print("     说明网关不校验模型归属，客户端自己不加门禁就会把 A 的票据发给 B")
    return False


def step_replay_cost(chat):
    print("\n== 3c. 回填的代价：同一段对话带/不带票据各发一次 ==")
    if not chat or not chat["ticket_fields"]:
        print("  第 2 步没有票据，跳过")
        return
    plain_assistant = {"role": "assistant", "content": "391"}
    with_ticket = dict(plain_assistant)
    with_ticket.update(chat["ticket_fields"])

    def tokens(assistant):
        result = post("/v1/chat/completions", {
            "model": MODEL,
            "messages": [{"role": "user", "content": PROMPT}, assistant, {"role": "user", "content": FOLLOW_UP}],
            "stream": False,
        })
        if failed(result):
            return None
        return (result.get("usage") or {}).get("prompt_tokens")

    without = tokens(plain_assistant)
    with_it = tokens(with_ticket)
    print("  不带票据 prompt_tokens:", without)
    print("  带票据   prompt_tokens:", with_it)
    if without and with_it:
        print(f"  → 一张票据折合 {with_it - without} 个输入 token（{len(json.dumps(chat['ticket_fields']))} 字符的 JSON）")


def step_responses():
    print("\n== 4. /responses + include:[\"reasoning.encrypted_content\"] ==")
    body = {
        "model": MODEL,
        "input": [{"role": "user", "content": [{"type": "input_text", "text": PROMPT}]}],
        "store": False,
        "include": ["reasoning.encrypted_content"],
    }
    result = post("/v1/responses", body)
    if failed(result):
        return None, True

    items = result.get("output") or []
    print("  output 条目:", [item.get("type") for item in items] or "(空)")
    encrypted = [item for item in items if item.get("encrypted_content")]
    if encrypted:
        print("  带 encrypted_content 的条目:", len(encrypted),
              "，首条长度", len(encrypted[0]["encrypted_content"]))
    else:
        print("  带 encrypted_content 的条目: 0")
    reasoning = [item for item in items if item.get("type") == "reasoning"]
    print("  reasoning 条目:", len(reasoning))
    for item in reasoning:
        print("    键:", list(item), "| summary:", snippet(item.get("summary"), 80))
    usage = result.get("usage") or {}
    print("  output_tokens_details:", usage.get("output_tokens_details"))
    print("  input_tokens_details:", usage.get("input_tokens_details"))
    return result, False


def step_responses_replay(first):
    print("\n== 5. 第一轮的 output 原样塞回第二轮（responses 侧回填） ==")
    items = list(first.get("output") or [])
    if not items:
        print("  第一轮没有 output，跳过")
        return True
    items.append({"role": "user", "content": [{"type": "input_text", "text": FOLLOW_UP}]})
    result = post("/v1/responses", {"model": MODEL, "input": items, "store": False,
                                    "include": ["reasoning.encrypted_content"]})
    if failed(result):
        print("  → 第二轮被拒：票据无法原样回放")
        return True
    print("  → 第二轮通过，output 条目:", [item.get("type") for item in result.get("output") or []])
    usage = result.get("usage") or {}
    print("  input_tokens_details:", usage.get("input_tokens_details"))
    return False


def main():
    if not KEY:
        print("需要 CMD_API_KEY 环境变量（Studio → API keys 里那个）", file=sys.stderr)
        return 2

    # 不短路：端点查不到也照样量一次 chat，答案在响应里而不在目录里
    failed_endpoints = step_endpoints()
    chat, failed_chat = step_chat()
    failed_stream = step_chat_stream()
    failed_replay = step_chat_replay(chat)
    step_foreign_replay(chat)
    step_replay_cost(chat)
    first, failed_responses = step_responses()
    failed_responses_replay = step_responses_replay(first) if first is not None else True

    print("\n结论怎么看：")
    print("  第 2 步有 reasoning_details / reasoning_content 且第 3 步收下 → chat 侧本来就能回填，")
    print("    缺的是我们适配层把它扔了（MEAI 只认 reasoning_content）。")
    print("  第 4 步有 encrypted_content 且第 5 步通过 → 加密票据在两条路上都有，/responses 不是唯一解。")
    print("  第 2、4 步都为空 → 这个模型在网关的两条路上都不给货，别在客户端继续找办法。")
    return 0 if not any([failed_endpoints, failed_chat, failed_stream, failed_replay,
                         failed_responses, failed_responses_replay]) else 1


if __name__ == "__main__":
    sys.exit(main())
