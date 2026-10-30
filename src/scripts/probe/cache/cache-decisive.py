#!/usr/bin/env python3
"""决定性实验:票据本身影不影响前缀缓存?亲和头在生产规模下有没有用?

三个问题,都用真实请求体(从日志里切)来测,不用玩具前缀:

  Q1  加/删 reasoning_details,prompt_tokens 变不变?
      变了 → 票据进了分词,进前缀,删掉必然打掉缓存
      没变 → 网关把它剥掉了,删掉不影响缓存(那"消失"就不是原因)
  Q2  生产规模下(7k+ token),带 x-session-id 头比不带命中率高吗?
      高 → 亲和头有用,现在的掉底是网关没按它路由
      一样 → 网关不认这个头,得换别的办法
  Q3  同样规模下 chat 与 responses 的命中率谁高?

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-decisive.py [--rounds 8]
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


def load_ca():
    here = os.path.dirname(os.path.abspath(__file__))
    spec = importlib.util.spec_from_file_location(
        "ca", os.path.join(here, "..", "..", "perf", "cache-audit.py"))
    ca = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(ca)
    return ca


def real_bodies(logs, model_filter):
    ca = load_ca()
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
            out.append(obj)
    return out


def send(path, body, affinity=None):
    headers = dict(UA)
    if affinity:
        headers["x-session-id"] = affinity
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(),
                                     headers=headers)
    try:
        with urllib.request.urlopen(request) as response:
            raw = response.read().decode("utf-8", "replace")
    except urllib.error.HTTPError:
        return None
    for line in raw.splitlines():
        if line.startswith("data: ") and '"usage"' in line:
            try:
                obj = json.loads(line[6:])
            except json.JSONDecodeError:
                continue
            u = obj.get("usage") or {}
            d = u.get("input_tokens_details") or u.get("prompt_tokens_details") or {}
            total = u.get("input_tokens") or u.get("prompt_tokens")
            return (total, d.get("cached_tokens", 0))
    try:
        obj = json.loads(raw)
        u = obj.get("usage") or {}
        d = u.get("input_tokens_details") or u.get("prompt_tokens_details") or {}
        return (u.get("input_tokens") or u.get("prompt_tokens"), d.get("cached_tokens", 0))
    except json.JSONDecodeError:
        return None


def strip_details(body):
    """把每条 assistant 上的 reasoning_details 摘掉,其余逐字不动"""
    copy = dict(body)
    copy["messages"] = [dict(m) for m in body["messages"]]
    for m in copy["messages"]:
        m.pop("reasoning_details", None)
    return copy


def with_details(body, ticket):
    copy = dict(body)
    copy["messages"] = [dict(m) for m in body["messages"]]
    for m in copy["messages"]:
        if m.get("role") == "assistant":
            m["reasoning_details"] = [ticket]
            break
    return copy


TICKET = {"type": "reasoning.encrypted", "data": "Q-PaD" + "x" * 300,
          "id": "rs_probe:rs_probe", "format": "probe-v1", "index": 0}


def stats(series):
    ok = [r for r in series if r]
    hits = sum(1 for r in ok if r[1] > 200)
    prompts = {r[0] for r in ok}
    return hits, len(ok), prompts


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--logs", default=os.path.expanduser("~/.uiharu/Logs"))
    ap.add_argument("--model", default="")
    ap.add_argument("--rounds", type=int, default=8)
    args = ap.parse_args()
    if not KEY:
        print("缺少 CMD_API_KEY")
        return 1

    bodies = real_bodies(args.logs, args.model)
    if not bodies:
        print("没切到真实请求体")
        return 1
    # 取最新的、生产规模最大的那份
    body = max(bodies, key=lambda b: len(json.dumps(b)))
    print("真实请求体: %d 条消息, %d 字节, prompt 约 %d token\n" %
          (len(body["messages"]), len(json.dumps(body)), 7000))

    print("=== Q1 票据对分词与缓存的影响 ===")
    plain = strip_details(body)
    tagged = with_details(body, TICKET)
    a = [send("/v1/chat/completions", tagged) for _ in range(args.rounds)]
    b = [send("/v1/chat/completions", plain) for _ in range(args.rounds)]
    ha, na, pa = stats(a)
    hb, nb, pb = stats(b)
    print("  带票据: prompt=%s 命中 %d/%d" % (sorted(pa), ha, na))
    print("  无票据: prompt=%s 命中 %d/%d" % (sorted(pb), hb, nb))
    print("  → prompt_tokens %s" % ("相同,票据没进分词" if pa == pb else "不同,票据进了分词"))

    print("\n=== Q2 亲和头在生产规模下有没有用 ===")
    key = body.get("prompt_cache_key") or "affinity-probe-1"
    with_aff = [send("/v1/chat/completions", body, key) for _ in range(args.rounds)]
    without = [send("/v1/chat/completions", body) for _ in range(args.rounds)]
    hw, nw, _ = stats(with_aff)
    ho, no, _ = stats(without)
    print("  带 x-session-id: 命中 %d/%d" % (hw, nw))
    print("  不带:           命中 %d/%d" % (ho, no))

    print("\n=== Q3 chat 与 responses 谁稳 ===")
    resp = {"model": body["model"], "max_output_tokens": 16, "store": False, "input": []}
    for m in body["messages"]:
        if not isinstance(m.get("content"), str):
            continue
        role = "assistant" if m["role"] == "assistant" else m["role"]
        resp["input"].append({"role": role,
                              "content": [{"type": "output_text" if role == "assistant"
                                           else "input_text", "text": m["content"]}]})
    cr = [send("/v1/chat/completions", body) for _ in range(args.rounds)]
    rr = [send("/v1/responses", resp) for _ in range(args.rounds)]
    hc, nc, _ = stats(cr)
    hr, nr, _ = stats(rr)
    print("  chat:      命中 %d/%d" % (hc, nc))
    print("  responses: 命中 %d/%d" % (hr, nr))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
