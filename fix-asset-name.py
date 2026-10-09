#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
修正 Release 附件的文件名。

之前上传时中文名「晾晒工具.exe」被编码成了 default.exe。
这个脚本把错误的附件删掉，用正确的名字重新上传。

用法：
    set GH_TOKEN=ghp_xxx
    python fix-asset-name.py
"""
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

REPO = "yi-yezhiqiu/snapline"
TAG = "v1.0.0"
HERE = os.path.dirname(os.path.abspath(__file__))
ASSET_PATH = os.path.join(HERE, "dist", "晾晒工具.exe")
WANT_NAME = "晾晒工具.exe"


def api(path, method="GET", data=None, raw=None, ctype="application/json"):
    token = os.environ.get("GH_TOKEN", "").strip()
    if not token:
        raise SystemExit("缺少 GH_TOKEN 环境变量")

    url = path if path.startswith("http") else "https://api.github.com" + path
    body = json.dumps(data).encode("utf-8") if data is not None else raw

    req = urllib.request.Request(url, data=body, method=method)
    req.add_header("Authorization", "Bearer " + token)
    req.add_header("User-Agent", "snapline-publish")
    req.add_header("Accept", "application/vnd.github+json")
    if body is not None:
        req.add_header("Content-Type", ctype)
        req.add_header("Content-Length", str(len(body)))

    try:
        with urllib.request.urlopen(req, timeout=180) as r:
            text = r.read().decode("utf-8", "replace")
            return r.status, (json.loads(text) if text.strip() else {})
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")
    except Exception as e:
        return 0, "%s: %s" % (type(e).__name__, e)


def main():
    print("=== 修正附件名 ===")

    # 取回 release
    status, rel = api("/repos/%s/releases/tags/%s" % (REPO, TAG))
    if status != 200:
        print("取 Release 失败: HTTP %s  %s" % (status, str(rel)[:300]))
        return 1

    print("Release id = %s" % rel["id"])
    assets = rel.get("assets", [])
    print("现有附件: %s" % (", ".join(a["name"] for a in assets) or "(无)"))

    # 删掉名字不对的
    for a in assets:
        if a["name"] != WANT_NAME:
            print("删除 %s (id=%s) ..." % (a["name"], a["id"]))
            st, _ = api("/repos/%s/releases/assets/%s" % (REPO, a["id"]), "DELETE")
            print("   HTTP %s" % st)
            if st != 204:
                print("   删除失败，继续")

    # 重新上传，文件名做百分号编码（GitHub 要求出现在 name= 参数里）
    if not os.path.isfile(ASSET_PATH):
        print("找不到 %s" % ASSET_PATH)
        return 1

    with open(ASSET_PATH, "rb") as fh:
        blob = fh.read()

    upload = rel["upload_url"].split("{")[0] + "?name=" + urllib.parse.quote(WANT_NAME)
    print("重新上传为: %s  (%.1f KB)" % (WANT_NAME, len(blob) / 1024.0))
    st, res = api(upload, "POST", raw=blob, ctype="application/octet-stream")
    print("   HTTP %s" % st)
    if st in (200, 201) and isinstance(res, dict):
        print("   名称: %s" % res["name"])
        print("   大小: %.1f KB" % (res["size"] / 1024.0))
        print("   下载: %s" % res["browser_download_url"])
    else:
        print("   失败: %s" % str(res)[:400])
        return 1

    # 复核
    print("\n=== 复核 ===")
    st, rel2 = api("/repos/%s/releases/tags/%s" % (REPO, TAG))
    if st == 200:
        for a in rel2.get("assets", []):
            print("  %s  %.1f KB  下载数=%d" % (a["name"], a["size"] / 1024.0, a["download_count"]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
