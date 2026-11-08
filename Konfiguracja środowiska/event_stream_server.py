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
MAX_CLUSTERS = 2

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
    Hand-only tracker built on Prophesee Metavision SDK CV algorithms (metavision_sdk_cv):
      1. ActivityNoiseFilterAlgorithm: Spatiotemporal C++ noise filter removing isolated sensor events.
      2. SparseOpticalFlowAlgorithm (FastObjects preset): Per-event optical flow vectors (vx, vy).
      3. Hand-Only Morphological & Silhouette Discriminator:
         - Detects and suppresses full-body sway (upright torso anchored at the bottom of the frame
           moving side-to-side without independent hand slashes).
         - Computes 2D interior event thickness (thick_px: >= 5 neighbors in 3x3) to separate dense 2D
           hands/palms/fists from thin 1D contours (forearms, sleeves, head, shoulders).
         - Groups active 8x8 cells into connected limb components and selects at most 1 hand hotspot
           per arm (highest thick5 * sqrt(fast5 + 1)), completely eliminating elbow and forearm markers.
         - Enforces strict hand verification (no hand detected -> zero clusters & zero motion_grid).
    """

    def __init__(
        self,
        width,
        height,
        denoise=True,
        min_speed=155.0,
        min_thickness=260.0,
        max_hands=2,
        sway_filter=True,
    ):
        self.width = width
        self.height = height
        self.denoise = bool(denoise)
        self.min_speed = float(np.clip(min_speed, 50.0, 450.0))
        self.min_thickness = float(np.clip(min_thickness, 100.0, 600.0))
        self.max_hands = int(np.clip(max_hands, 1, 2))
        self.sway_filter = bool(sway_filter)

        self.gh = height // 8
        self.gw = width // 8

        # Native Metavision spatiotemporal noise filter (8ms window)
        self.activity_filter = mv_cv.ActivityNoiseFilterAlgorithm(width, height, 8000)
        self.filtered_buf = self.activity_filter.get_empty_output_buffer()

        # Native Metavision Sparse Optical Flow tuned for fast moving objects (hands/blades)
        self.flow_algo = mv_cv.SparseOpticalFlowAlgorithm(
            width, height, mv_cv.SparseOpticalFlowConfigPreset.FastObjects
        )
        self.flow_buf = self.flow_algo.get_empty_output_buffer()

        # Full-body sway suppression state
        self.body_sway_cooldown = 0
        self.torso_x_center = width * 0.5
        self.torso_half_w = 130.0
        self.cell_ema = np.zeros((self.gh, self.gw), dtype=np.float32)

    def process_slice(self, events):
        frame = get_light_wood_bg(self.height, self.width).copy()
        motion_grid = np.zeros((GRID_H, GRID_W), dtype=np.uint8)

        if events.size == 0:
            self.cell_ema *= 0.88
            if self.body_sway_cooldown > 0:
                self.body_sway_cooldown -= 1
            frame_BottomUp = np.ascontiguousarray(np.flipud(frame))
            return frame_BottomUp, motion_grid, []

        # 1. Run Metavision ActivityNoiseFilterAlgorithm on raw event buffer
        self.activity_filter.process_events(events, self.filtered_buf)
        clean_evs = self.filtered_buf.numpy()
        render_evs = clean_evs if self.denoise else events

        h, w, gh, gw = self.height, self.width, self.gh, self.gw
        act = np.zeros((h, w), dtype=np.uint8)
        if clean_evs.size > 0:
            act[clean_evs["y"], clean_evs["x"]] = 1

        n3 = cv2.boxFilter(act, ddepth=cv2.CV_16U, ksize=(3, 3), normalize=False)

        # 2. Render visual frame on the light wooden background (horizontally mirrored for natural selfie view)
        if render_evs.size > 0:
            if self.denoise:
                x = clean_evs["x"]
                y = clean_evs["y"]
                p = clean_evs["p"].astype(bool)
                pol = np.zeros((h, w), dtype=np.uint8)
                pol[y[~p], x[~p]] = 1
                pol[y[p], x[p]] = 2

                n5 = cv2.boxFilter(act, ddepth=cv2.CV_16U, ksize=(5, 5), normalize=False)
                vis_mask = (act > 0) & (n3 >= 2) & (n5 >= 5)

                # Mirror horizontally so player movement on screen matches real-world left/right
                vis_mask_mirrored = np.fliplr(vis_mask)
                pol_mirrored = np.fliplr(pol)

                frame[vis_mask_mirrored & (pol_mirrored == 2)] = (255, 252, 240)
                frame[vis_mask_mirrored & (pol_mirrored == 1)] = (26, 14, 5)
            else:
                rx = (w - 1) - render_evs["x"]
                ry = render_evs["y"]
                rp = render_evs["p"].astype(bool)
                frame[ry[rp], rx[rp]] = (255, 252, 240)
                frame[ry[~rp], rx[~rp]] = (26, 14, 5)

        frame_BottomUp = np.ascontiguousarray(np.flipud(frame))

        # 3. Run Metavision SparseOpticalFlowAlgorithm on denoised events
        self.flow_algo.process_events(self.filtered_buf, self.flow_buf)
        fl = self.flow_buf.numpy()

        # 4. Compute 8x8 cell fill and 2D interior thickness (thick_px: >= 5 neighbors in 3x3)
        thick_px = ((act > 0) & (n3 >= 5)).astype(np.uint8)
        cell_fill = act[: gh * 8, : gw * 8].reshape(gh, 8, gw, 8).sum(axis=(1, 3)).astype(np.float32)
        cell_thick = thick_px[: gh * 8, : gw * 8].reshape(gh, 8, gw, 8).sum(axis=(1, 3)).astype(np.float32)

        sil_mask = cell_fill >= 4.0
        dense_mask = cell_fill >= 15.0
        tot_sil = int(sil_mask.sum())
        tot_dense = int(dense_mask.sum())

        # 5. Detect Upright Body Silhouette & Full-Body Sway
        torso_col_mask = np.zeros((gh, gw), dtype=bool)
        has_upright_torso = False

        if self.sway_filter:
            r_hip0, r_hip1 = int(gh * 0.76), gh
            r_chst0, r_chst1 = int(gh * 0.33), int(gh * 0.76)
            r_up0, r_up1 = 0, int(gh * 0.50)

            col_hip = sil_mask[r_hip0:r_hip1, :].sum(axis=0) >= 4
            col_chest = sil_mask[r_chst0:r_chst1, :].sum(axis=0) >= 6
            col_upper = sil_mask[r_up0:r_up1, :].sum(axis=0) >= 4

            k5 = np.ones((1, 5), dtype=np.uint8)
            hip_d = cv2.dilate(col_hip.astype(np.uint8)[None, :], k5)[0] > 0
            chest_d = cv2.dilate(col_chest.astype(np.uint8)[None, :], k5)[0] > 0
            upper_d = cv2.dilate(col_upper.astype(np.uint8)[None, :], np.ones((1, 9), dtype=np.uint8))[0] > 0

            torso_cols = np.where(hip_d & chest_d)[0]
            upright_cols = np.where(hip_d & chest_d & upper_d)[0]
            upper_sil = int(sil_mask[r_up0:r_up1, :].sum())
            bottom_base_sil = int(sil_mask[r_hip0:r_hip1, :].sum())

            if torso_cols.size >= 14 and upright_cols.size >= 8 and upper_sil >= 75 and bottom_base_sil >= 85:
                tx0, tx1 = int(torso_cols[0]), int(torso_cols[-1])
                tw_cols = tx1 - tx0 + 1
                if tw_cols >= 15:
                    has_upright_torso = True
                    cx_px = 0.5 * (tx0 + tx1 + 1) * 8.0
                    self.torso_x_center = 0.6 * self.torso_x_center + 0.4 * cx_px
                    self.torso_half_w = float(np.clip(tw_cols * 4.0 + 16.0, 110.0, 165.0))
                    torso_col_mask[int(gh * 0.26) : gh, max(0, tx0 - 3) : min(gw, tx1 + 4)] = True
                    hx0 = max(0, int((self.torso_x_center - 90.0) // 8))
                    hx1 = min(gw, int((self.torso_x_center + 90.0) // 8) + 1)
                    torso_col_mask[0 : int(gh * 0.37), hx0:hx1] = True

            torso_sil = int(sil_mask[torso_col_mask].sum()) if has_upright_torso else 0
            outside_sil = tot_sil - torso_sil

            is_heavy_body_sway = has_upright_torso and bottom_base_sil >= 105 and (
                (tot_sil >= 650) or (tot_dense >= 335)
            )

            if is_heavy_body_sway:
                self.body_sway_cooldown = 18  # ~300 ms cooldown across sway turnarounds
            elif self.body_sway_cooldown > 0:
                if (
                    torso_cols.size >= 11
                    and tot_sil >= 240
                    and bottom_base_sil >= 65
                    and outside_sil < max(130, torso_sil * 0.45)
                ):
                    self.body_sway_cooldown = max(self.body_sway_cooldown - 1, 10)
                elif tot_sil >= 380 and bottom_base_sil >= 95:
                    self.body_sway_cooldown = max(self.body_sway_cooldown - 1, 8)
                else:
                    self.body_sway_cooldown -= 1
        else:
            self.body_sway_cooldown = 0

        self.cell_ema = 0.85 * self.cell_ema + 0.15 * cell_fill

        # During full-body sway, suppress all cluster and grid registration
        if self.body_sway_cooldown > 0:
            return frame_BottomUp, motion_grid, []

        # 6. Bin fast optical flow events into the 8x8 cell grid
        # Scale per-event flow speed cutoff with user-configured min_speed (default 155 -> 135 px/s)
        event_speed_cutoff = max(55.0, self.min_speed * (135.0 / 155.0))
        cell_fast = np.zeros((gh, gw), dtype=np.float32)
        cell_vx = np.zeros((gh, gw), dtype=np.float32)
        cell_vy = np.zeros((gh, gw), dtype=np.float32)
        if fl.size > 0:
            sp = np.hypot(fl["vx"], fl["vy"])
            ok = (
                (sp >= event_speed_cutoff)
                & (fl["x"] >= 0)
                & (fl["x"] < gw * 8)
                & (fl["y"] >= 0)
                & (fl["y"] < gh * 8)
            )
            fx = fl["x"][ok] // 8
            fy = fl["y"][ok] // 8
            np.add.at(cell_fast, (fy, fx), 1.0)
            np.add.at(cell_vx, (fy, fx), fl["vx"][ok])
            np.add.at(cell_vy, (fy, fx), fl["vy"][ok])

        # 7. Compute 5x5 cell (40x40 px) neighborhood aggregates for hand hotspot detection
        dense5 = cv2.boxFilter(dense_mask.astype(np.float32), ddepth=-1, ksize=(5, 5), normalize=False)
        thick5 = cv2.boxFilter(cell_thick, ddepth=-1, ksize=(5, 5), normalize=False)
        fast5 = cv2.boxFilter(cell_fast, ddepth=-1, ksize=(5, 5), normalize=False)
        vx5 = cv2.boxFilter(cell_vx, ddepth=-1, ksize=(5, 5), normalize=False)
        vy5 = cv2.boxFilter(cell_vy, ddepth=-1, ksize=(5, 5), normalize=False)

        valid_cell = cell_fill >= 6.0
        if has_upright_torso:
            # Exclude hollow/slow torso and head silhouette cells so connected arm components focus on hands
            weak_torso = torso_col_mask & ((thick5 < 560.0) | (fast5 < 190.0) | (dense5 < 17.0))
            valid_cell[weak_torso] = False

        limb_mask = valid_cell.astype(np.uint8)
        limb_mask = cv2.morphologyEx(limb_mask, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))

        n_comp, labels, stats, _ = cv2.connectedComponentsWithStats(limb_mask, connectivity=8)

        # Thresholds scaled from user settings (exact match to verified defaults at min_thickness=260, min_speed=155)
        thick_ratio = self.min_thickness / 260.0
        speed_ratio = self.min_speed / 155.0
        req_dense = float(np.clip(12.0 * thick_ratio, 6.0, 22.0))
        req_thick = self.min_thickness
        req_fast = float(np.clip(65.0 * (0.65 * thick_ratio + 0.35 * speed_ratio), 25.0, 140.0))

        candidates = []
        for c in range(1, n_comp):
            area = int(stats[c, cv2.CC_STAT_AREA])
            if area < 10:
                continue
            x0, y0, cw, ch, _ = stats[c]
            cmask = labels == c

            # If a single component spans very wide across both arms (>= 300 px), split into Left and Right arms
            sub_masks = []
            if cw * 8 >= 300:
                mid_x = x0 + cw // 2
                left_m = cmask.copy()
                left_m[:, max(x0, mid_x - 4) :] = False
                right_m = cmask.copy()
                right_m[:, : min(x0 + cw, mid_x + 4)] = False
                sub_masks.extend([left_m, right_m])
            else:
                sub_masks.append(cmask)

            for sm in sub_masks:
                if int(sm.sum()) < 8:
                    continue

                # Select the single strongest hand hotspot in this arm component
                score_map = np.where(sm, thick5 * np.sqrt(fast5 + 1.0), 0.0)
                by, bx = np.unravel_index(np.argmax(score_map), score_map.shape)
                d_val = float(dense5[by, bx])
                th_val = float(thick5[by, bx])
                f_val = float(fast5[by, bx])

                # Strict hand verification: dense 2D core + fast optical flow
                if d_val < req_dense or th_val < req_thick or f_val < req_fast:
                    continue

                mvx = float(vx5[by, bx]) / max(f_val, 1.0)
                mvy = float(vy5[by, bx]) / max(f_val, 1.0)
                mspd = float(np.hypot(mvx, mvy))
                if mspd < self.min_speed:
                    continue

                px = (bx + 0.5) * 8.0
                py = (by + 0.5) * 8.0

                if self.sway_filter:
                    # Suppress chest/torso interior unless extreme hand activity is right in front of the chest
                    if has_upright_torso and torso_col_mask[by, bx]:
                        if th_val < 650.0 or f_val < 240.0 or d_val < 19.0:
                            continue

                    # Suppress lower-hip/waist edge when lowering arms at bottom of screen
                    if py >= h * 0.885 and (th_val < 600.0 or f_val < 220.0):
                        continue

                    # Suppress head region (top-center above chest/neck silhouette)
                    if py <= h * 0.325 and abs(px - self.torso_x_center) <= 95.0:
                        below_sil = int(
                            sil_mask[
                                min(gh - 1, by + 4) : min(gh, by + 22),
                                max(0, bx - 6) : min(gw, bx + 7),
                            ].sum()
                        )
                        if below_sil >= 18 and (th_val < 900.0 or f_val < 500.0):
                            continue

                # Mirror horizontally (u -> 1 - u, vu -> -vu) to match the mirrored video feed
                u = float(np.clip(1.0 - (px / w), 0.0, 1.0))
                v = float(np.clip(1.0 - (py / h), 0.0, 1.0))
                vu = float(-mvx / w)
                vv = float(-mvy / h)
                intensity = float(min(1.0, (th_val / 600.0) * (mspd / 200.0)))
                score = th_val * np.sqrt(f_val) * (mspd / 200.0)
                candidates.append((score, px, py, u, v, vu, vv, intensity))

        # 8. Non-maximum suppression: at most max_hands, minimum 130 px apart
        candidates.sort(key=lambda item: item[0], reverse=True)
        selected = []
        for cand in candidates:
            _, px, py, u, v, vu, vv, intensity = cand
            if any(np.hypot(px - sx, py - sy) < 130.0 for sx, sy, *_ in selected):
                continue
            selected.append((px, py, u, v, vu, vv, intensity))
            if len(selected) >= self.max_hands:
                break

        # Sort selected hands left-to-right (by u) so 2-hand trail assignment stays consistent
        selected.sort(key=lambda item: item[2])
        clusters = [(u, v, vu, vv, intensity) for _, _, u, v, vu, vv, intensity in selected]

        # 9. Populate motion_grid ONLY around verified tracked hands ("no hand detected -> no registration")
        for u, v, _, _, intensity in clusters:
            gx = int(np.clip(u * GRID_W, 0, GRID_W - 1))
            gy = int(np.clip(v * GRID_H, 0, GRID_H - 1))
            val = int(np.clip(intensity * 255.0, 110.0, 255.0))
            y0 = max(0, gy - 1)
            y1 = min(GRID_H, gy + 2)
            x0 = max(0, gx - 1)
            x1 = min(GRID_W, gx + 2)
            motion_grid[y0:y1, x0:x1] = np.maximum(motion_grid[y0:y1, x0:x1], val)

        return frame_BottomUp, motion_grid, clusters


def send_all(sock, data):
    view = memoryview(data)
    total = 0
    while total < len(data):
        sent = sock.send(view[total:])
        if sent == 0:
            raise ConnectionError("Socket connection broken")
        total += sent


def stream_to_client(conn, mode, raw_file, delta_t, denoise, min_speed, min_thickness, max_hands, sway_filter):
    while True:
        input_arg = "" if mode == "live" else raw_file
        try:
            iterator = EventsIterator(input_path=input_arg, delta_t=delta_t)
            height, width = iterator.get_size()
        except Exception as exc:
            print(f"[EventSlash Server] Error opening EventsIterator (mode={mode}, path='{input_arg}'): {exc}", flush=True)
            time.sleep(0.5)
            break

        tracker = MetavisionHandTracker(
            width,
            height,
            denoise=denoise,
            min_speed=min_speed,
            min_thickness=min_thickness,
            max_hands=max_hands,
            sway_filter=sway_filter,
        )
        print(
            f"[EventSlash Server] Streaming {mode} ({width}x{height}) | "
            f"min_speed={min_speed:.0f}px/s, min_thickness={min_thickness:.0f}, "
            f"max_hands={max_hands}, sway_filter={sway_filter}, denoise={denoise}",
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
    parser.add_argument("--min-speed", type=float, default=155.0, help="Minimum hand speed in px/s required to register")
    parser.add_argument("--min-thickness", type=float, default=260.0, help="Minimum 2D hand thickness score required")
    parser.add_argument("--max-hands", type=int, default=2, help="Maximum simultaneous hands tracked (1 or 2)")
    parser.add_argument("--sway-filter", type=int, default=1, help="1 to suppress full-body sway & torso/head, 0 to disable")
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
                stream_to_client(
                    conn,
                    args.mode,
                    args.file,
                    args.delta_t,
                    bool(args.denoise),
                    args.min_speed,
                    args.min_thickness,
                    args.max_hands,
                    bool(args.sway_filter),
                )
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
