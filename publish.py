#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
用 GitHub REST API 发布 Release 并上传 exe 附件。

为什么用 Python 而不是 PowerShell：
  这台机器上 .NET 的 TLS 栈（SChannel）在受限令牌下会报
  "No credentials are available in the security package"，
  Invoke-RestMethod 连不上 api.github.com。
  Python 自带 OpenSSL，不走 SChannel，直接可用。

用法（token 从环境变量读，不落盘）：
    set GH_TOKEN=ghp_xxx
    python publish.py
"""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

REPO = "yi-yezhiqiu/snapline"
TAG = "v1.0.0"
TARGET = "main"
RELEASE_NAME = "Snapline v1.0.0 · 晾晒工具"

HERE = os.path.dirname(os.path.abspath(__file__))
ASSET_PATH = os.path.join(HERE, "dist", "晾晒工具.exe")
NOTES_PATH = os.path.join(HERE, "RELEASE_NOTES.md")

# 让话题更容易被搜到
TOPICS = [
    "windows", "screenshot", "screenshot-tool", "wpf", "csharp",
    "clipboard", "productivity", "tray-icon", "dotnet-framework", "desktop-app",
]


def api(path, method="GET", data=None, raw=None, ctype="application/json"):
    """发一个 GitHub API 请求。path 以 http 开头时直接用，否则拼 api.github.com。"""
    token = os.environ.get("GH_TOKEN", "").strip()
    if not token:
        raise SystemExit("缺少 GH_TOKEN 环境变量")

    url = path if path.startswith("http") else "https://api.github.com" + path
    body = json.dumps(data).encode("utf-8") if data is not None else raw

    req = urllib.request.Request(url, data=body, method=method)
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("User-Agent", "snapline-publish")          # 缺了会 403
    req.add_header("Accept", "application/vnd.github+json")
    if body is not None:
        req.add_header("Content-Type", ctype)
        req.add_header("Content-Length", str(len(body)))

    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            text = r.read().decode("utf-8", "replace")
            return r.status, (json.loads(text) if text.strip() else {})
    except urllib.error.HTTPError as e:
        detail = e.read().decode("utf-8", "replace")
        return e.code, detail
    except Exception as e:
        return 0, "%s: %s" % (type(e).__name__, e)


def main():
    print("=== 发布 %s ===" % TAG)

    # ---------- 检查本地文件 ----------
    if not os.path.isfile(ASSET_PATH):
        raise SystemExit("找不到附件：%s（先跑 build.ps1）" % ASSET_PATH)
    size = os.path.getsize(ASSET_PATH)
    print("附件: %s  (%.1f KB)" % (os.path.basename(ASSET_PATH), size / 1024.0))

    with open(NOTES_PATH, "r", encoding="utf-8") as fh:
        notes = fh.read()

    # ---------- 1. 创建 Release ----------
    print("\n[1/4] 创建 Release ...")
    status, res = api("/repos/%s/releases" % REPO, "POST", {
        "tag_name": TAG,
        "target_commitish": TARGET,
        "name": RELEASE_NAME,
        "body": notes,
        "draft": False,
        "prerelease": False,
    })
    print("      HTTP %s" % status)

    if status == 422:
        print("      标签已存在 Release，取回它")
        status, res = api("/repos/%s/releases/tags/%s" % (REPO, TAG))
        print("      HTTP %s" % status)

    if status not in (200, 201) or not isinstance(res, dict):
        print("      失败：%s" % str(res)[:400])
        return 1

    rel_id = res["id"]
    html = res.get("html_url", "")
    print("      已创建: %s" % html)

    # ---------- 2. 上传附件 ----------
    print("\n[2/4] 上传 exe ...")
    existing = {a["name"] for a in res.get("assets", [])}
    asset_name = os.path.basename(ASSET_PATH)

    if asset_name in existing:
        print("      附件已存在，跳过")
        for a in res["assets"]:
            if a["name"] == asset_name:
                print("      下载地址: %s" % a["browser_download_url"])
    else:
        with open(ASSET_PATH, "rb") as fh:
            blob = fh.read()
        upload = res["upload_url"].split("{")[0] + "?name=" + urllib.parse.quote(asset_name)
        status, res2 = api(upload, "POST", raw=blob, ctype="application/octet-stream")
        print("      HTTP %s" % status)
        if status in (200, 201) and isinstance(res2, dict):
            print("      已上传: %s  (%.1f KB)" % (res2["name"], res2["size"] / 1024.0))
            print("      下载地址: %s" % res2["browser_download_url"])
        else:
            print("      上传失败：%s" % str(res2)[:400])
            print("      Release 已建好，可在网页上手动拖入 exe")

    # ---------- 3. 设话题（提高搜索曝光）----------
    print("\n[3/4] 设置 topics ...")
    status, res3 = api("/repos/%s/topics" % REPO, "PUT", {
        "names": TOPICS,
    })
    print("      HTTP %s" % status)
    if status == 200 and isinstance(res3, dict):
        print("      已设置: %s" % ", ".join(res3.get("names", [])))
    else:
        print("      失败：%s" % str(res3)[:300])

    # ---------- 4. 复核 ----------
    print("\n[4/4] 复核 releases 接口 ...")
    status, rels = api("/repos/%s/releases" % REPO)
    if status == 200 and isinstance(rels, list):
        print("      Release 数量 = %d" % len(rels))
        for r in rels:
            print("      - %s  %s" % (r["tag_name"], r["name"]))
            for a in r.get("assets", []):
                print("          附件 %s  %.1f KB  下载数=%d" % (
                    a["name"], a["size"] / 1024.0, a["download_count"]))
    else:
        print("      复核失败：%s" % str(rels)[:300])

    print("\n=== 完成 ===")
    print("Release 页面: %s" % html)
    print("下载直链: https://github.com/%s/releases/latest" % REPO)
    return 0


if __name__ == "__main__":
    sys.exit(main())
