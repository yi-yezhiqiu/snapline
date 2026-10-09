#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
把 Release 附件整理成最终状态：只保留一个 ASCII 命名的 exe。

（GitHub 不接受非 ASCII 的附件名，中文会被剥掉并回落成 default.exe，
所以附件名用 Snapline.exe，中文名只在说明正文里出现。）
"""
import json, os, sys, urllib.error, urllib.parse, urllib.request

REPO = "yi-yezhiqiu/snapline"
TAG = "v1.0.0"
HERE = os.path.dirname(os.path.abspath(__file__))
ASSET = os.path.join(HERE, "dist", "晾晒工具.exe")
FINAL_NAME = "Snapline.exe"


def api(path, method="GET", data=None, raw=None, ctype="application/json"):
    token = os.environ["GH_TOKEN"].strip()
    url = path if path.startswith("http") else "https://api.github.com" + path
    body = json.dumps(data).encode() if data is not None else raw
    req = urllib.request.Request(url, data=body, method=method)
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("User-Agent", "probe")
    req.add_header("Accept", "application/vnd.github+json")
    if body is not None:
        req.add_header("Content-Type", ctype)
        req.add_header("Content-Length", str(len(body)))
    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            t = r.read().decode("utf-8", "replace")
            return r.status, (json.loads(t) if t.strip() else {})
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:
        return 0, "%s: %s" % (type(e).__name__, e)


def main():
    print("=== 整理 Release 附件 ===")
    st, rel = api("/repos/%s/releases/tags/%s" % (REPO, TAG))
    if st != 200:
        print("取 Release 失败", st, rel); return 1

    have_final = False
    for a in rel.get("assets", []):
        if a["name"] == FINAL_NAME:
            print("已存在正确附件: %s" % a["name"])
            have_final = True
            continue
        st2, _ = api("/repos/%s/releases/assets/%s" % (REPO, a["id"]), "DELETE")
        print("删除 %-16s -> HTTP %s" % (a["name"], st2))

    if not have_final:
        with open(ASSET, "rb") as fh:
            blob = fh.read()
        base = rel["upload_url"].split("{")[0]
        url = base + "?name=" + urllib.parse.quote(FINAL_NAME)
        print("上传为 %s ..." % FINAL_NAME)
        st3, res = api(url, "POST", raw=blob, ctype="application/octet-stream")
        print("  HTTP %s -> %s" % (st3, res.get("name") if isinstance(res, dict) else str(res)[:200]))
        if st3 not in (200, 201):
            return 1

    # 同时更新 Release 正文，让下载说明用对的文件名
    notes_path = os.path.join(HERE, "RELEASE_NOTES.md")
    with open(notes_path, "r", encoding="utf-8") as fh:
        notes = fh.read()
    st4, _ = api("/repos/%s/releases/%s" % (REPO, rel["id"]), "PATCH", {
        "body": notes,
        "name": "Snapline v1.0.0 · 晾晒工具",
    })
    print("更新 Release 正文 -> HTTP %s" % st4)

    # 复核
    st5, rel2 = api("/repos/%s/releases/tags/%s" % (REPO, TAG))
    print("\n=== 最终状态 ===")
    if st5 == 200:
        print("Release: %s" % rel2["html_url"])
        for a in rel2.get("assets", []):
            print("  附件 %s  %.1f KB  下载数=%d" % (a["name"], a["size"] / 1024.0, a["download_count"]))
            print("        %s" % a["browser_download_url"])
    return 0


if __name__ == "__main__":
    sys.exit(main())
