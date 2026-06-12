#!/usr/bin/env python3
"""
mock_server.py — 临时 mock API 服务器，用于调试 LeagueTool 上报数据

支持 BG（8 人）和构筑（2 人）两种 mode。
启动后监听 5000 端口，接收 /api/plugin/* 请求，打印完整请求数据并返回 200。

用法：
  python mock_server.py              # 默认 0.0.0.0:5000
  python mock_server.py 8080         # 指定端口

LeagueTool 的 config.json 中 apiBaseUrl 设为 http://localhost:5000
"""

import json
import random
import string
import sys
import uuid
from datetime import datetime, timezone
from http.server import HTTPServer, BaseHTTPRequestHandler


def gen_verification_code(length=8):
    """生成随机验证码（字母+数字）"""
    chars = string.ascii_uppercase + string.digits
    return ''.join(random.choices(chars, k=length))


def is_constructed_mode(mode):
    """判断是否为构筑模式"""
    return mode in ("STD", "WILD")


class MockState:
    """模拟服务端状态"""
    def __init__(self):
        self.games = {}  # gameUuid -> {players, placements}
        self.verification_codes = {}  # playerId -> code

    def initialize_player(self, data):
        player_id = data.get("playerId", "")
        account_hi = data.get("accountIdHi", "")
        account_lo = data.get("accountIdLo", "")
        region = data.get("region", "UNKNOWN")

        if not player_id:
            return {"error": "playerId 为空"}, 400

        print(f"  👤 玩家: {player_id} (Hi={account_hi}, Lo={account_lo}, Region={region})")

        # 每个玩家固定一个验证码（和正式服务器一致）
        if player_id not in self.verification_codes:
            self.verification_codes[player_id] = gen_verification_code()
            print(f"  🔑 生成验证码: {player_id} → {self.verification_codes[player_id]}")
        else:
            print(f"  🔑 返回已有验证码: {player_id} → {self.verification_codes[player_id]}")

        return {"ok": True, "verificationCode": self.verification_codes[player_id]}, 200

    def check_league(self, data):
        server_uuid = str(uuid.uuid4())
        code = gen_verification_code()

        player_id = data.get("playerId", "")
        mode = data.get("mode", "unknown")
        players = data.get("players", {})

        if is_constructed_mode(mode):
            print(f"  📋 构筑对局: {server_uuid}")
            print(f"     本地: {player_id}")
            for lo, p in players.items():
                tag = p.get("battleTag", "???")
                hero = p.get("heroName", "???")
                print(f"     Lo={lo} {tag} ({hero})")
        else:
            account_lo_list = data.get("accountIdLoList", [])
            print(f"  📋 酒馆对局: {server_uuid} ({len(account_lo_list)} 人)")
            for lo in account_lo_list:
                p = players.get(lo, {})
                name = p.get("displayName", p.get("battleTag", "???"))
                hero = p.get("heroName", "???")
                print(f"     Lo={lo} {name} ({hero})")

        self.games[server_uuid] = {
            "mode": mode,
            "playerId": player_id,
            "players": players,
            "placements": {},
            "code": code,
        }

        return {"isLeague": True, "verificationCode": code, "gameUuid": server_uuid}

    def update_placement(self, data):
        game_uuid = data.get("gameUuid", "")
        mode = data.get("mode", "unknown")
        placements = data.get("placements", [])
        account_lo = str(data.get("accountIdLo", ""))
        placement = data.get("placement", 0)
        reconnect_times = data.get("reconnectTimes", [])
        other_placements = data.get("otherPlacements", [])

        # 构筑模式：自动创建 game（如果不存在）
        if game_uuid not in self.games:
            if is_constructed_mode(mode):
                self.games[game_uuid] = {"mode": mode, "players": {}, "placements": {}}
            else:
                return {"error": "对局不存在"}, 404

        game = self.games[game_uuid]

        if placements:
            # placements 数组格式（构筑 / 双方同时上报）
            for p in placements:
                lo = str(p.get("accountIdLo", ""))
                pl = p.get("placement", 0)
                game["placements"][lo] = pl

            if is_constructed_mode(mode):
                print(f"  🏁 构筑对局结束: {game_uuid}")
                for p in placements:
                    lo = str(p.get("accountIdLo", ""))
                    tag = p.get("playerId", "???")
                    pl = p.get("placement", 0)
                    result = "胜利" if pl == 1 else "失败" if pl == 2 else f"第{pl}名"
                    print(f"     {tag} (Lo={lo}) → {result}")
            else:
                total = len(game["placements"])
                player_count = len(game.get("players", {}))
                finalized = total >= player_count if player_count > 0 else True
                print(f"  {'🏁' if finalized else '📝'} 酒馆对局: {game_uuid}")
                for p in placements:
                    lo = str(p.get("accountIdLo", ""))
                    pl = p.get("placement", 0)
                    print(f"     Lo={lo} → 第{pl}名")
        else:
            # 旧版：单玩家上报（BG）
            game["placements"][account_lo] = placement

            for op in other_placements:
                op_lo = str(op.get("accountIdLo", ""))
                op_placement = op.get("placement", 0)
                if op_lo and op_placement and op_lo not in game["placements"]:
                    game["placements"][op_lo] = op_placement

            total = len(game["placements"])
            reconnect_info = ""
            if reconnect_times:
                reconnect_info = f" | 🔄 断线{len(reconnect_times)}次"
            print(f"  📝 排名已记录: Lo={account_lo} → 第{placement}名 ({total}/8){reconnect_info}")

        return {"ok": True, "finalized": True}, 200

    def report_game_stats(self, data):
        """处理游戏统计上报"""
        account_lo = str(data.get("accountIdLo", ""))
        player_id = data.get("playerId", "")
        rating = data.get("rating", 0)
        placement = data.get("placement", 0)
        board_state = data.get("boardState", [])
        rating_change = data.get("ratingChange")
        trinkets = data.get("trinkets", [])
        anomaly_dbf_id = data.get("anomalyDbfId", 0)

        print(f"  📊 游戏统计: Lo={account_lo} ({player_id})")
        print(f"     Rating: {rating}, 排名: {placement}")
        if anomaly_dbf_id:
            print(f"     畸变 DBF ID: {anomaly_dbf_id}")
        print(f"     阵容: {len(board_state)} 个随从")
        for m in board_state:
            print(f"       {m.get('cardId', '?')} {m.get('attack', 0)}/{m.get('health', 0)} T{m.get('techLevel', 0)}")
        if trinkets:
            print(f"     饰品: {', '.join(trinkets)}")
        if rating_change:
            print(f"     RatingChange: {rating_change.get('oldRating', 0)} -> {rating_change.get('newRating', 0)} ({rating_change.get('change', 0):+d})")
        else:
            print(f"     RatingChange: 未获取到")

        return {"ok": True}, 200


state = MockState()


class MockHandler(BaseHTTPRequestHandler):
    """处理所有请求，打印请求数据"""

    def do_POST(self):
        content_len = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(content_len) if content_len > 0 else b""

        # 解析 JSON
        try:
            data = json.loads(body) if body else {}
        except json.JSONDecodeError:
            data = {"_raw": body.decode("utf-8", errors="replace")}

        # 打印到控制台
        ts = datetime.now().strftime("%H:%M:%S")
        print(f"\n{'='*60}")
        print(f"[{ts}] POST {self.path}")
        print(f"{'─'*60}")
        print(f"Headers:")
        for k, v in self.headers.items():
            print(f"  {k}: {v}")
        print(f"Body ({content_len} bytes):")
        print(json.dumps(data, indent=2, ensure_ascii=False))

        # 保存到日志文件
        with open("mock_requests.log", "a", encoding="utf-8") as f:
            f.write(f"\n[{ts}] POST {self.path}\n")
            f.write(f"Headers: {dict(self.headers)}\n")
            f.write(f"Body: {json.dumps(data, ensure_ascii=False)}\n")

        # 根据路径返回不同的模拟响应
        if "/check-league" in self.path:
            resp = state.check_league(data)
            status = 200
        elif "/initialize-player" in self.path:
            resp, raw_status = state.initialize_player(data)
            status = int(raw_status)
        elif "/update-placement" in self.path:
            resp, raw_status = state.update_placement(data)
            status = int(raw_status)
        elif "/report-game-stats" in self.path:
            resp, raw_status = state.report_game_stats(data)
            status = int(raw_status)
        else:
            resp, raw_status = {"ok": True}, 200
            status = int(raw_status)

        print(f"Response: {json.dumps(resp, ensure_ascii=False)}")
        print(f"{'='*60}")

        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(resp, ensure_ascii=False).encode("utf-8"))

    def do_GET(self):
        ts = datetime.now().strftime("%H:%M:%S")
        print(f"[{ts}] GET {self.path}")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(b'{"ok": true}')

    def log_message(self, format, *args):
        """抑制默认的 access log，只保留我们自己的输出"""
        pass


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 5000
    server = HTTPServer(("0.0.0.0", port), MockHandler)
    print(f"🎯 Mock API 服务器启动: http://0.0.0.0:{port}")
    print(f"   接收 /api/plugin/initialize-player、check-league、update-placement")
    print(f"   请求记录保存到 mock_requests.log")
    print(f"   Ctrl+C 停止\n")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n⏹ 已停止")


if __name__ == "__main__":
    main()
