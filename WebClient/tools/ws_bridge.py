"""Local stand-in for the VPS's websockify: accepts browser WebSocket connections and pipes them to the game server's raw TCP port.
    python tools/ws_bridge.py [listen_port=8125] [target_host=104.152.50.196] [target_port=2050]
The browser client then uses  ?game=ws://127.0.0.1:8125  (see wwwroot/main.js)."""
import asyncio, sys
import websockets

listen = int(sys.argv[1]) if len(sys.argv) > 1 else 8125
host = sys.argv[2] if len(sys.argv) > 2 else '104.152.50.196'
port = int(sys.argv[3]) if len(sys.argv) > 3 else 2050


async def handler(ws, path=None):   # older websockets (Ubuntu's 9.x) pass (ws, path)
    reader, writer = await asyncio.open_connection(host, port)
    print('bridge: client connected ->', host, port, flush=True)

    async def up():
        try:
            async for msg in ws:
                writer.write(msg if isinstance(msg, bytes) else msg.encode())
                await writer.drain()
        finally:
            writer.close()

    async def down():
        try:
            while True:
                data = await reader.read(65536)
                if not data:
                    break
                await ws.send(data)
        finally:
            await ws.close()

    await asyncio.gather(up(), down(), return_exceptions=True)
    print('bridge: closed', flush=True)


async def main():
    async with websockets.serve(handler, '127.0.0.1', listen, max_size=None, ping_interval=20):
        print('ws bridge on ws://127.0.0.1:%d -> %s:%d' % (listen, host, port), flush=True)
        await asyncio.Future()

asyncio.run(main())
