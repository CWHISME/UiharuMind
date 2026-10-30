#!/usr/bin/env python3
"""两件决定修法的事:

A. 上游的缓存键按字节还是按 token?
   同一份 body,先发「无票据」版(冷),再发「加票据」版(只多一个 reasoning_details 键):
     加票据版也命中 → 键按 token,票据不进前缀,我们的改动无害
     加票据版不命中 → 键按字节,票据让前缀变了,每轮换一次票据就冷一次
   这决定「票据要不要跟着消息长期保留」。

B. 生产规模下 chat 与 responses 谁稳?
   拿真实请求体(7k+ token)改写成 responses 的 input 形状,各发 N 次,
   只统计成功响应里的命中率(网关会偶发 4xx/5xx,那与缓存无关)。

用法:
  CMD_API_KEY=xxx python3 src/scripts/probe/cache/cache-keytest.py [--rounds 8]
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


def send(path, body):
    request = urllib.request.Request(BASE + path, data=json.dumps(body).encode(),
                                     headers=UA)
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
            return (u.get("input_tokens") or u.get("prompt_tokens"), d.get("cached_tokens", 0))
    try:
        obj = json.loads(raw)
        u = obj.get("usage") or {}
        d = u.get("input_tokens_details") or u.get("prompt_tokens_details") or {}
        return (u.get("input_tokens") or u.get("prompt_tokens"), d.get("cached_tokens", 0))
    except json.JSONDecodeError:
        return None


def hit_ratio(series):
    ok = [r for r in series if r]
    hits = sum(1 for r in ok if r[1] > 200)
    return hits, len(ok)


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
    body = max(bodies, key=lambda b: len(json.dumps(b)))

    # ---- A. 缓存键按字节还是按 token ----
    print("=== A. 缓存键:字节 还是 token ===")
    plain = dict(body)
    plain["messages"] = [dict(m) for m in body["messages"]]
    for m in plain["messages"]:
        m.pop("reasoning_details", None)
    tagged = dict(body)
    tagged["messages"] = [dict(m) for m in body["messages"]]
    # 加一个真票据形状的字段(长度与真票据相当)
    for m in tagged["messages"]:
        if m.get("role") == "assistant":
            m["reasoning_details"] = [{"type": "reasoning.encrypted", "data": "Q-PaD" + "x" * 300,
                                        "id": "rs_probe:rs_probe", "format": "probe-v1",
                                        "index": 0}]
            break

    # 用一个独一无二的 prompt_cache_key,确保这一组不借前面实验的缓存
    plain["prompt_cache_key"] = "keytest-plain-01"
    tagged["prompt_cache_key"] = "keytest-tagged-01"
    p_seq = [send("/v1/chat/completions", plain) for _ in range(3)]
    t_seq = [send("/v1/chat/completions", tagged) for _ in range(3)]
    print("  无票据版: %s" % p_seq)
    print("  带票据版: %s" % t_seq)
    hp, np_ = hit_ratio(p_seq)
    ht, nt = hit_ratio(t_seq)
    print("  无票据 %d/%d  带票据 %d/%d" % (hp, np_, ht, nt))
    if ht >= hp:
        print("  → 键按 token:票据不进前缀,加/删都不影响缓存")
    else:
        print("  → 键按字节:票据进了前缀,换票据就冷一次")

    # ---- B. chat 与 responses ----
    print("\n=== B. 生产规模 chat vs responses(各 %d 次) ===" % args.rounds)
    resp = {"model": body["model"], "max_output_tokens": 16, "store": False, "input": []}
    for m in body["messages"]:
        if not isinstance(m.get("content"), str):
            continue
        role = "assistant" if m["role"] == "assistant" else m["role"]
        resp["input"].append({
            "role": role,
            "content": [{"type": "output_text" if role == "assistant" else "input_text",
                         "text": m["content"]}]})
    c_seq = [send("/v1/chat/completions", body) for _ in range(args.rounds)]
    r_seq = [send("/v1/responses", resp) for _ in range(args.rounds)]
    print("  chat:      %s" % c_seq)
    print("  responses: %s" % r_seq)
    hc, nc = hit_ratio(c_seq)
    hr, nr = hit_ratio(r_seq)
    print("  chat %d/%d   responses %d/%d" % (hc, nc, hr, nr))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
