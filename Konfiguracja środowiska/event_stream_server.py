import argparse
import os
import socket
import struct
import sys
import time
import traceback
import cv2
import numpy as np
from metavision_core.event_io import EventsIterator
import metavision_sdk_cv as mv_cv

MAGIC = b"EVSL"
GRID_W = 32
GRID_H = 24
MAX_CLUSTERS = 4

_WOOD_BG_CACHE = {}


def get_light_wood_bg(height, width):
    """
    Generates and caches a warm light-wood plank background (RGB24) of shape (height, width, 3)
    so the event camera feed appears on a light wooden board instead of a flat grey background.
    """
    key = (height, width)
    if key in _WOOD_BG_CACHE:
        return _WOOD_BG_CACHE[key]

    y = np.linspace(0.0, 1.0, height, dtype=np.float32)[:, None]
    x = np.linspace(0.0, 1.0, width, dtype=np.float32)[None, :]

    plank_pos = x * 8.0
    plank_idx = np.floor(plank_pos)
    plank_frac = plank_pos - plank_idx

    grain1 = np.sin(y * 38.0 + plank_idx * 1.9 + np.sin(x * 45.0 + plank_idx * 3.1) * 1.6) * 0.5 + 0.5
    grain2 = np.cos(y * 110.0 + x * 24.0 + plank_idx * 2.7) * 0.5 + 0.5
    knot_wave = np.sin(y * 14.0 + plank_idx * 4.2) * 0.5 + 0.5

    plank_tone = 0.94 + 0.10 * np.sin(plank_idx * 2.3 + 0.5)
    tone = (0.86 + 0.10 * grain1 + 0.05 * grain2 + 0.04 * knot_wave) * plank_tone

    seam_dist = np.minimum(plank_frac, 1.0 - plank_frac)
    seam_factor = np.clip(seam_dist / 0.022, 0.68, 1.0)
    tone = tone * seam_factor

    r = np.clip(218.0 * tone, 0, 255).astype(np.uint8)
    g = np.clip(178.0 * tone, 0, 255).astype(np.uint8)
    b = np.clip(124.0 * tone, 0, 255).astype(np.uint8)

    wood_bg = np.stack([r, g, b], axis=-1)
    _WOOD_BG_CACHE[key] = wood_bg
    return wood_bg


class MetavisionHandTracker:
    """
    Uses native Prophesee Metavision SDK CV algorithms (metavision_sdk_cv):
      1. ActivityNoiseFilterAlgorithm: Spatiotemporal C++ noise filter removing isolated sensor events.
      2. SparseOpticalFlowAlgorithm (FastObjects preset): Computes per-event optical flow vectors
         (vx, vy) and cluster IDs (id, center_x, center_y) to track fast hand/blade slashes.
    """

    def __init__(self, width, height, denoise=True):
        self.width = width
        self.height = height
        self.denoise = denoise

        # Native Metavision spatiotemporal noise filter (8ms window)
        self.activity_filter = mv_cv.ActivityNoiseFilterAlgorithm(width, height, 8000)
        self.filtered_buf = self.activity_filter.get_empty_output_buffer()

        # Native Metavision Sparse Optical Flow tuned for fast moving objects (hands/blades)
        self.flow_algo = mv_cv.SparseOpticalFlowAlgorithm(
            width, height, mv_cv.SparseOpticalFlowConfigPreset.FastObjects
        )
        self.flow_buf = self.flow_algo.get_empty_output_buffer()

    def process_slice(self, events):
        frame = get_light_wood_bg(self.height, self.width).copy()
        cell_h = self.height // GRID_H
        cell_w = self.width // GRID_W

        if events.size == 0:
            frame_BottomUp = np.ascontiguousarray(np.flipud(frame))
            motion_grid = np.zeros((GRID_H, GRID_W), dtype=np.uint8)
            return frame_BottomUp, motion_grid, []

        # 1. Run Metavision ActivityNoiseFilterAlgorithm on raw event buffer
        self.activity_filter.process_events(events, self.filtered_buf)
        clean_evs = self.filtered_buf.numpy() if self.denoise else events

        # 2. Render visual frame on the light wooden background
        if clean_evs.size > 0:
            x = clean_evs["x"]
            y = clean_evs["y"]
            p = clean_evs["p"].astype(bool)

            if self.denoise:
                # Additional spatial check so any isolated 1-2px specks after temporal filter stay wood-colored
                act = np.zeros((self.height, self.width), dtype=np.uint8)
                pol = np.zeros((self.height, self.width), dtype=np.uint8)
                act[y, x] = 1
                pol[y[~p], x[~p]] = 1
                pol[y[p], x[p]] = 2

                n3 = cv2.boxFilter(act, ddepth=cv2.CV_16U, ksize=(3, 3), normalize=False)
                n5 = cv2.boxFilter(act, ddepth=cv2.CV_16U, ksize=(5, 5), normalize=False)
                vis_mask = (act > 0) & (n3 >= 2) & (n5 >= 5)

                frame[vis_mask & (pol == 2)] = (255, 252, 240)
                frame[vis_mask & (pol == 1)] = (26, 14, 5)
            else:
                frame[y[p], x[p]] = (255, 252, 240)
                frame[y[~p], x[~p]] = (26, 14, 5)

        frame_BottomUp = np.ascontiguousarray(np.flipud(frame))

        # 3. Run Metavision SparseOpticalFlowAlgorithm on denoised events to track moving hands
        self.flow_algo.process_events(self.filtered_buf, self.flow_buf)
        fln = self.flow_buf.numpy()

        flow_map = np.zeros((self.height, self.width), dtype=np.float32)
        clusters = []

        if fln.size > 0:
            speed = np.hypot(fln["vx"], fln["vy"])
            valid = (
                (fln["x"] >= 0)
                & (fln["x"] < self.width)
                & (fln["y"] >= 0)
                & (fln["y"] < self.height)
                & (speed >= 55.0)
            )
            vfl = fln[valid]
            vspeed = speed[valid]

            if vfl.size > 0:
                # Populate optical-flow weighted activity map for the 32x24 grid
                speed_weight = np.clip(vspeed / 160.0, 0.5, 3.0)
                flow_map[vfl["y"], vfl["x"]] = speed_weight

                # Extract top clusters from Metavision SparseOpticalFlow cluster IDs
                uids, counts = np.unique(vfl["id"], return_counts=True)
                good_mask = counts >= 12
                good_ids = uids[good_mask]
                good_counts = counts[good_mask]

                candidates = []
                for cid, cnt in zip(good_ids, good_counts):
                    sub = vfl[vfl["id"] == cid]
                    xs = sub["x"]
                    ys = sub["y"]
                    vxs = sub["vx"]
                    vys = sub["vy"]

                    # If a single optical-flow cluster spans wide across both arms, extract left & right hand tips
                    if cnt >= 180 and (xs.max() - xs.min()) >= 110:
                        x_min, x_max = int(xs.min()), int(xs.max())
                        left_sel = xs <= (x_min + 45)
                        right_sel = xs >= (x_max - 45)
                        for sel in (left_sel, right_sel):
                            scnt = int(np.count_nonzero(sel))
                            if scnt >= 12:
                                cx = float(xs[sel].mean())
                                cy = float(ys[sel].mean())
                                mvx = float(vxs[sel].mean())
                                mvy = float(vys[sel].mean())
                                mspd = float(np.hypot(mvx, mvy))
                                u = float(np.clip(cx / self.width, 0.0, 1.0))
                                v = float(np.clip(1.0 - (cy / self.height), 0.0, 1.0))
                                vu = float(mvx / self.width)
                                vv = float(-mvy / self.height)
                                intensity = float(min(1.0, (scnt / 80.0) * (mspd / 180.0)))
                                candidates.append((scnt * mspd * 1.4, u, v, vu, vv, intensity))
                    else:
                        cx = float(xs.mean())
                        cy = float(ys.mean())
                        mvx = float(vxs.mean())
                        mvy = float(vys.mean())
                        mspd = float(np.hypot(mvx, mvy))
                        u = float(np.clip(cx / self.width, 0.0, 1.0))
                        v = float(np.clip(1.0 - (cy / self.height), 0.0, 1.0))
                        vu = float(mvx / self.width)
                        vv = float(-mvy / self.height)
                        intensity = float(min(1.0, (cnt / 120.0) * (mspd / 180.0)))
                        candidates.append((cnt * mspd, u, v, vu, vv, intensity))

                candidates.sort(key=lambda item: item[0], reverse=True)
                for _, u, v, vu, vv, intensity in candidates[:MAX_CLUSTERS]:
                    clusters.append((u, v, vu, vv, intensity))

        # Flip flow_map vertically to match Unity bottom-up viewport coordinates (v=0 at bottom)
        flow_BottomUp = np.flipud(flow_map)
        trimmed = flow_BottomUp[: GRID_H * cell_h, : GRID_W * cell_w]
        cell_flow = trimmed.reshape(GRID_H, cell_h, GRID_W, cell_w).sum(axis=(1, 3))
        motion_grid = np.clip(cell_flow * 2.2, 0.0, 255.0).astype(np.uint8)

        return frame_BottomUp, motion_grid, clusters


def send_all(sock, data):
    view = memoryview(data)
    total = 0
    while total < len(data):
        sent = sock.send(view[total:])
        if sent == 0:
            raise ConnectionError("Socket connection broken")
        total += sent


def stream_to_client(conn, mode, raw_file, delta_t, denoise):
    while True:
        input_arg = "" if mode == "live" else raw_file
        try:
            iterator = EventsIterator(input_path=input_arg, delta_t=delta_t)
            height, width = iterator.get_size()
        except Exception as exc:
            print(f"[EventSlash Server] Error opening EventsIterator (mode={mode}, path='{input_arg}'): {exc}", flush=True)
            time.sleep(0.5)
            break

        tracker = MetavisionHandTracker(width, height, denoise=denoise)
        print(
            f"[EventSlash Server] Streaming {mode} ({width}x{height}) using Metavision SDK CV "
            f"(ActivityNoiseFilter + SparseOpticalFlow), denoise={denoise}",
            flush=True,
        )

        frame_interval = delta_t / 1_000_000.0
        next_frame_time = time.perf_counter()

        for events in iterator:
            frame_rgb, motion_grid, clusters = tracker.process_slice(events)

            frame_bytes = frame_rgb.tobytes()
            grid_bytes = motion_grid.tobytes()

            cluster_count = len(clusters)
            header = struct.pack(
                "<4sHHHHHi",
                MAGIC,
                width,
                height,
                GRID_W,
                GRID_H,
                cluster_count,
                len(frame_bytes),
            )

            cluster_buf = bytearray()
            for u, v, vu, vv, intensity in clusters:
                cluster_buf += struct.pack("<5f", u, v, vu, vv, intensity)

            packet = header + bytes(cluster_buf) + grid_bytes + frame_bytes
            send_all(conn, packet)

            if mode != "live":
                next_frame_time += frame_interval
                sleep_dur = next_frame_time - time.perf_counter()
                if sleep_dur > 0:
                    time.sleep(sleep_dur)
                else:
                    next_frame_time = time.perf_counter()

        if mode == "live":
            break
        print("[EventSlash Server] RAW file finished, looping from start...", flush=True)


def main():
    parser = argparse.ArgumentParser(description="EventSlash Metavision TCP Stream Server")
    parser.add_argument("--mode", choices=["live", "raw"], default="raw")
    parser.add_argument("--file", default="raw_recordings/recording_2026-10-14_11-42-46.raw")
    parser.add_argument("--port", type=int, default=5055)
    parser.add_argument("--delta-t", type=int, default=16666)
    parser.add_argument("--denoise", type=int, default=1, help="1 to filter isolated noise pixels from frame, 0 for raw")
    parser.add_argument("--once", action="store_true", help="Exit after first client disconnects")
    args = parser.parse_args()

    script_dir = os.path.dirname(os.path.abspath(__file__))
    os.chdir(script_dir)

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind(("127.0.0.1", args.port))
    server.listen(2)
    print(f"[EventSlash Server] Listening on 127.0.0.1:{args.port} (default mode={args.mode}, denoise={bool(args.denoise)})", flush=True)

    try:
        while True:
            conn, addr = server.accept()
            conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            print(f"[EventSlash Server] Client connected from {addr}", flush=True)
            try:
                stream_to_client(conn, args.mode, args.file, args.delta_t, bool(args.denoise))
            except (ConnectionResetError, ConnectionAbortedError, BrokenPipeError, ConnectionError):
                print("[EventSlash Server] Client disconnected.", flush=True)
            except Exception:
                traceback.print_exc()
            finally:
                try:
                    conn.close()
                except Exception:
                    pass
            if args.once:
                break
    finally:
        server.close()


if __name__ == "__main__":
    main()
