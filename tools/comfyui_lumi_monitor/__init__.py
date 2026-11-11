"""
Traceability metadata
Version: 1.0
Generated / Last updated / Content coverage through: 2026-11-10 (Asia/Shanghai)
Source type: RAW | Lumi local monitoring request; LOCAL-WORK | bridge implementation
Organizing node: Windows / Codex Desktop / Codex / Lumi independent companion
Original sources: ComfyUI 0.39.2 runtime observation and ComfyUI progress registry API
https://docs.comfy.org/development/comfyui-server/comms_routes
https://github.com/Comfy-Org/ComfyUI/blob/master/comfy_execution/progress.py
"""

from ipaddress import ip_address
from math import isfinite
import re

from aiohttp import web

import folder_paths
from comfy_execution.progress import NodeState, get_progress_state
from server import PromptServer


NODE_CLASS_MAPPINGS = {}

_LOCAL_HOST = re.compile(r"(?:127\.0\.0\.1|localhost|\[::1\])(?::[0-9]{1,5})?", re.IGNORECASE)
_FORWARDING_HEADERS = {
    "forwarded", "via", "x-real-ip", "x-client-ip", "true-client-ip", "cf-connecting-ip",
}
_NO_STORE = {"Cache-Control": "no-store"}


def _is_direct_loopback(request):
    hosts = request.headers.getall("Host", [])
    if len(hosts) != 1 or not _LOCAL_HOST.fullmatch(hosts[0]):
        return False

    if any(
        name.lower() in _FORWARDING_HEADERS or name.lower().startswith("x-forwarded-")
        for name in request.headers
    ):
        return False

    local_origins = {f"http://{hosts[0].lower()}", f"https://{hosts[0].lower()}"}
    if any(origin.lower() not in local_origins for origin in request.headers.getall("Origin", [])):
        return False

    transport = request.transport
    peer = transport.get_extra_info("peername") if transport else None
    if not isinstance(peer, tuple) or not peer:
        return False
    try:
        address = ip_address(peer[0])
    except ValueError:
        return False
    return (getattr(address, "ipv4_mapped", None) or address).is_loopback


def _current_progress():
    running, _ = PromptServer.instance.prompt_queue.get_current_queue_volatile()
    if not running:
        return None

    registry = get_progress_state()
    prompt_id = registry.prompt_id
    if not isinstance(prompt_id, str) or not prompt_id or not any(job[1] == prompt_id for job in running):
        return None

    # Copy the registry's dict and each candidate entry before examining the worker's data.
    active_nodes = []
    for node, entry in registry.nodes.copy().items():
        state = entry.copy()
        if state.get("state") is NodeState.Running:
            active_nodes.append((node, state))
            if len(active_nodes) > 1:
                return None
    if not active_nodes:
        return None

    node, state = active_nodes[0]
    value, maximum = state.get("value"), state.get("max")
    if not isinstance(node, str) or not node:
        return None
    if type(value) not in (int, float) or type(maximum) not in (int, float):
        return None
    # ComfyUI starts a node with a 0/1 placeholder before any progress update.
    if maximum <= 0 or not 0 <= value <= maximum or (value == 0 and maximum == 1):
        return None
    try:
        if not isfinite(value) or not isfinite(maximum):
            return None
    except OverflowError:
        return None

    if registry is not get_progress_state() or registry.nodes.get(node, {}).copy() != state:
        return None

    return {"prompt_id": prompt_id, "node": node, "value": value, "max": maximum}


@PromptServer.instance.routes.get("/lumi/monitor")
async def _lumi_monitor(request):
    if not _is_direct_loopback(request):
        raise web.HTTPForbidden(headers=_NO_STORE)

    return web.json_response(
        {"output_directory": folder_paths.get_output_directory(), "progress": _current_progress()},
        headers=_NO_STORE,
    )
