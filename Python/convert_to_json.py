# ===== import ===== #
import json
import math
import argparse
from pathlib import Path
from typing import List, Tuple, Dict, Optional
import numpy as np
import cv2

# ======================================================= #
#                    Global constants
# ======================================================= #
M_PER_PX      = 0.01
CEILING_H     = 2.6
DOOR_H        = 2.1
WINDOW_H      = 0.8
WINDOW_Y      = 1.0

# Thinning 및 기하 처리 파라미터
RDP_EPS       = 1.0
MIN_SEG_PX    = 6
MERGE_GAP_PX  = 24.0
PERP_TOL_PX   = 3.0
THICK_MIN_PX  = 2.0
THICK_MAX_PX  = 60.0

# Floor/Room 파라미터
FLOOR_APPROX_EPS = 2.0
FLOOR_MIN_AREA   = 1200.0
ADJ_TOUCH_THICK  = 3
SMALL_ROOM_RATIO = 0.03

# Unit Conversion
_PYEONG_TO_M2 = 3.305785 

NEI8 = [(-1,-1),(-1,0),(-1,1),(0,-1),(0,1),(1,-1),(1,0),(1,1)]

# ======================================================= #
#                    Geometry utilities
# ======================================================= #
def snap45(phi_deg: float) -> float:
    """
    Snap an angle to the nearest 45-degree increment (mod 180).

    Args:
        phi_deg (float): Angle in degrees.

    Returns:
        float: Snapped angle in degrees in [0, 180).
    """
    a = ((phi_deg % 180.0) + 180.0) % 180.0
    return (round(a / 45.0) * 45.0) % 180.0

def order_ccw_vertices_xy(quad_xy):
    """
    Order four vertices in counter-clockwise order around their centroid.

    Args:
        quad_xy (list[tuple[float, float]]): 4 points (x, y).

    Returns:
        list[tuple[float, float]]: Ordered points in CCW order.
    """
    if len(quad_xy) != 4:
        return quad_xy

    center_x = sum(pt[0] for pt in quad_xy) / 4.0
    center_y = sum(pt[1] for pt in quad_xy) / 4.0

    ordered = sorted(quad_xy, key=lambda pt: math.atan2(pt[1] - center_y, pt[0] - center_x))

    # Ensure CCW using a signed area-like check.
    signed_area2 = 0.0
    for idx in range(4):
        x_curr, y_curr = ordered[idx]
        x_next, y_next = ordered[(idx + 1) % 4]
        signed_area2 += (x_next - x_curr) * (y_next + y_curr)

    if signed_area2 < 0.0:
        ordered = list(reversed(ordered))

    return ordered

def ensure_cw(poly):
    """
    Ensure polygon winding is clockwise (CW).

    Args:
        poly (list[tuple[float, float]]): Polygon vertices (x, y).

    Returns:
        list[tuple[float, float]]: CW-wound polygon vertices.
    """
    a = 0.0
    for i in range(len(poly)):
        x1,y1 = poly[i]; x2,y2 = poly[(i+1)%len(poly)]
        a += x1*y2 - x2*y1
    return poly if a < 0 else list(reversed(poly))

def xy_to_m_rect(rect_xy: List[Tuple[float,float]]):
    """
    Convert a rectangle in pixel XY coordinates into metric XZ vertices.

    Note:
        The output uses (x, y, z) where y is kept at 0.0.

    Args:
        rect_xy (list[tuple[float, float]]): 4 vertices (x, y) in pixels.

    Returns:
        list[dict]: 4 vertex dicts with fields {x, y, z} in meters.
    """
    return [{"x": float(x*M_PER_PX), "y": 0.0, "z": float(y*M_PER_PX)} for (x,y) in rect_xy]

def mask_color(img_bgr: np.ndarray, kind: str) -> np.ndarray:
    """
    Extract a binary mask for a color region in HSV.

    Args:
        img_bgr (np.ndarray): Input BGR image (H, W, 3), uint8.
        kind (str): "green" for doors, otherwise treated as "red" for windows.

    Returns:
        np.ndarray: Binary mask (H, W), uint8 in {0,255}.
    """
    hsv = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2HSV)

    if kind == "green":
        # Door (green) region.
        color_mask = cv2.inRange(
            hsv,
            np.array([35, 80, 50], dtype=np.uint8),
            np.array([85, 255, 255], dtype=np.uint8),
        )
    else:
        # Window (red) region.
        # Red wraps around hue boundaries (near 0 and near 180), so we combine two ranges.
        color_mask = cv2.inRange(
            hsv,
            np.array([0, 100, 60], dtype=np.uint8),
            np.array([10, 255, 255], dtype=np.uint8),
        )
        color_mask |= cv2.inRange(
            hsv,
            np.array([170, 100, 60], dtype=np.uint8),
            np.array([180, 255, 255], dtype=np.uint8),
        )

    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
    color_mask = cv2.morphologyEx(color_mask, cv2.MORPH_OPEN, kernel, iterations=1)
    color_mask = cv2.morphologyEx(color_mask, cv2.MORPH_CLOSE, kernel, iterations=1)
    return color_mask

def clip_doors_auto_kernel(img_bgr: np.ndarray) -> np.ndarray:
    """
    Clip door pixels so that wall continuity is preserved.

    The input is a color-coded floor plan image. This function:
        - Detects walls (near-white) and doors (green) in BGR space.
        - Closes the wall mask with kernels sized from the largest door extent.
        - Removes door pixels that lie outside the closed wall region.

    Args:
        img_bgr (np.ndarray): Input BGR image (H, W, 3), uint8.

    Returns:
        np.ndarray: Image with door pixels clipped (same shape/dtype as input).
    """
    result_img = img_bgr.copy()

    lower_white = np.array([200, 200, 200])
    upper_white = np.array([255, 255, 255])
    mask_wall = cv2.inRange(img_bgr, lower_white, upper_white)

    lower_green = np.array([0, 150, 0])
    upper_green = np.array([100, 255, 100])
    mask_door = cv2.inRange(img_bgr, lower_green, upper_green)

    contours, _ = cv2.findContours(mask_door, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    max_door_side = 0
    if contours:
        for cnt in contours:
            x, y, w, h = cv2.boundingRect(cnt)
            current_max_side = max(w, h)
            if current_max_side > max_door_side:
                max_door_side = current_max_side
    
    kernel_length = max_door_side + 5 if max_door_side > 0 else 5

    horiz_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (kernel_length, 5))
    vert_kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (5, kernel_length))

    closed_horiz = cv2.morphologyEx(mask_wall, cv2.MORPH_CLOSE, horiz_kernel)
    closed_vert = cv2.morphologyEx(mask_wall, cv2.MORPH_CLOSE, vert_kernel)

    full_wall_mask = cv2.bitwise_or(closed_horiz, closed_vert)
    clipped_door_mask = cv2.bitwise_and(mask_door, full_wall_mask)

    result_img[mask_door > 0] = [0, 0, 0]
    result_img[clipped_door_mask > 0] = [0, 255, 0]

    return result_img

def thinning(binary_img: np.ndarray, door_mask: np.ndarray = None) -> np.ndarray:
    """
    Perform thinning to produce a wall skeleton image.

    The thinning step uses a Zhang-Suen style iterative deletion scheme:
        - Each iteration consists of two sub-iterations with slightly different deletion rules.
        - The algorithm runs until no further pixels are removed.

    If door_mask is provided, endpoints that touch door pixels are extended in the
    current direction to bridge gaps across door regions, improving wall continuity
    during subsequent path tracing.

    Args:
        binary_img (np.ndarray): Binary wall mask (H, W), uint8 (0/255).
        door_mask (np.ndarray | None): Door mask (H, W), uint8 (0/255).

    Returns:
        np.ndarray: Skeleton image (H, W), uint8 (0/255).
    """
    skeleton_bin = (binary_img > 0).astype(np.uint8)
    previous_state = np.zeros_like(skeleton_bin)

    def _shift_neighbors(img01: np.ndarray):
        """
        Build neighbor images in the original P2..P9 order used by the legacy implementation.

        NOTE:
            The P2..P9 indexing is kept intact to avoid changing behavior.
            This order is used only for computing neighbor counts and transition counts.
        """
        p2 = np.roll(img01, -1, axis=0)
        p3 = np.roll(np.roll(img01, -1, axis=0), 1, axis=1)
        p4 = np.roll(img01, 1, axis=1)
        p5 = np.roll(np.roll(img01, 1, axis=0), 1, axis=1)
        p6 = np.roll(img01, 1, axis=0)
        p7 = np.roll(np.roll(img01, 1, axis=0), -1, axis=1)
        p8 = np.roll(img01, -1, axis=1)
        p9 = np.roll(np.roll(img01, -1, axis=0), -1, axis=1)
        return p2, p3, p4, p5, p6, p7, p8, p9

    def _transition_count(neighbor_list) -> np.ndarray:
        """
        Count 0->1 transitions along the circular neighbor sequence.

        Args:
            neighbor_list (list[np.ndarray]): 8 neighbor images [p2..p9] with values {0,1}.

        Returns:
            np.ndarray: Transition count image (H, W), uint8.
        """
        seq = list(neighbor_list) + [neighbor_list[0]]
        transitions = np.zeros_like(skeleton_bin, dtype=np.uint8)
        for i in range(8):
            transitions += ((seq[i] == 0) & (seq[i + 1] == 1)).astype(np.uint8)
        return transitions

    def _sub_iteration(img01: np.ndarray, step: int) -> np.ndarray:
        """
        Perform one Zhang-Suen sub-iteration (step 1 or step 2).

        Args:
            img01 (np.ndarray): Current skeleton (H, W), uint8 in {0,1}.
            step (int): 1 or 2.

        Returns:
            np.ndarray: Updated skeleton (H, W), uint8 in {0,1}.
        """
        p2, p3, p4, p5, p6, p7, p8, p9 = _shift_neighbors(img01)

        neighbor_count = (p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9).astype(np.uint8)
        transitions = _transition_count([p2, p3, p4, p5, p6, p7, p8, p9])

        base_condition = (
            (img01 == 1)
            & (neighbor_count >= 2)
            & (neighbor_count <= 6)
            & (transitions == 1)
        )

        if step == 1:
            step_condition = ((p2 * p4 * p6) == 0) & ((p4 * p6 * p8) == 0)
        else:
            step_condition = ((p2 * p4 * p8) == 0) & ((p2 * p6 * p8) == 0)

        delete_mask = base_condition & step_condition

        updated = img01.copy()
        updated[delete_mask] = 0
        return updated

    while True:
        after_step1 = _sub_iteration(skeleton_bin, step=1)
        after_step2 = _sub_iteration(after_step1, step=2)

        if np.array_equal(after_step2, previous_state):
            skeleton_bin = after_step2
            break

        previous_state = after_step2.copy()
        skeleton_bin = after_step2

    skeleton = (skeleton_bin * 255).astype(np.uint8)

    # ------------------------------------------------------ #
    # Optional endpoint extension across door regions.
    # ------------------------------------------------------ #
    if door_mask is not None:
        door_bin = (door_mask > 0).astype(np.uint8)
        skeleton_bin = (skeleton > 0).astype(np.uint8)

        height, width = skeleton_bin.shape
        _, endpoints = degree_map(skeleton_bin)

        for endpoint_row, endpoint_col in endpoints:
            is_adjacent_to_door = False
            for d_row, d_col in NEI8:
                neighbor_row = endpoint_row + d_row
                neighbor_col = endpoint_col + d_col
                if 0 <= neighbor_row < height and 0 <= neighbor_col < width and door_bin[neighbor_row, neighbor_col] > 0:
                    is_adjacent_to_door = True
                    break

            if not is_adjacent_to_door:
                continue

            endpoint_neighbors = list(neighbors(skeleton_bin, endpoint_row, endpoint_col))
            if not endpoint_neighbors:
                continue

            neighbor_row, neighbor_col = endpoint_neighbors[0]
            step_row = endpoint_row - neighbor_row
            step_col = endpoint_col - neighbor_col
            if step_row == 0 and step_col == 0:
                continue

            cursor_row = endpoint_row
            cursor_col = endpoint_col

            while True:
                cursor_row += step_row
                cursor_col += step_col

                if cursor_row < 0 or cursor_row >= height or cursor_col < 0 or cursor_col >= width:
                    break

                if door_bin[cursor_row, cursor_col] > 0:
                    skeleton[cursor_row, cursor_col] = 255
                    skeleton_bin[cursor_row, cursor_col] = 1
                    continue

                if binary_img[cursor_row, cursor_col] > 0:
                    skeleton[cursor_row, cursor_col] = 255
                    skeleton_bin[cursor_row, cursor_col] = 1

                break

    return skeleton

def neighbors(img: np.ndarray, row: int, col: int):
    """
    Yield 8-connected neighbor coordinates for a binary image.

    Args:
        img (np.ndarray): Binary image (H, W), uint8 in {0,1} or {0,255}.
        row (int): Pixel row index.
        col (int): Pixel column index.

    Yields:
        tuple[int, int]: (neighbor_row, neighbor_col) for each foreground neighbor.
    """
    height, width = img.shape
    for d_row, d_col in NEI8:
        neighbor_row = row + d_row
        neighbor_col = col + d_col
        if 0 <= neighbor_row < height and 0 <= neighbor_col < width and img[neighbor_row, neighbor_col] > 0:
            yield neighbor_row, neighbor_col

def degree_map(skeleton_bin: np.ndarray):
    """
    Compute degree (number of 8-neighbors) for each foreground skeleton pixel.

    Args:
        skeleton_bin (np.ndarray): Skeleton binary image (H, W), uint8 in {0,1}.

    Returns:
        tuple[np.ndarray, list[tuple[int,int]]]:
            - degree (np.ndarray): Degree map (H, W), int16.
            - endpoints (list[tuple[int,int]]): Pixels where degree == 1, as (row, col).
    """
    padded = np.pad(skeleton_bin, ((1, 1), (1, 1)), mode="constant")
    degree = np.zeros_like(skeleton_bin, dtype=np.int16)

    rows, cols = np.where(skeleton_bin > 0)
    for row, col in zip(rows, cols):
        padded_row = row + 1
        padded_col = col + 1

        neighbor_count = 0
        for d_row, d_col in NEI8:
            if padded[padded_row + d_row, padded_col + d_col] > 0:
                neighbor_count += 1

        degree[row, col] = neighbor_count

    endpoint_rows, endpoint_cols = np.where((skeleton_bin > 0) & (degree == 1))
    endpoints = list(zip(endpoint_rows, endpoint_cols))
    return degree, endpoints

def trace_paths(sk: np.ndarray) -> List[List[Tuple[int, int]]]:
    """
    Trace polyline paths from a skeleton image.

    The function prefers walking from endpoints first, then collects remaining loops.

    Args:
        sk (np.ndarray): Skeleton image (H, W), uint8 (0/255).

    Returns:
        list[list[tuple[int, int]]]: Paths, each a list of (row, col) pixels.
    """
    skeleton_bin = (sk > 0).astype(np.uint8)
    visited = np.zeros_like(skeleton_bin, dtype=np.uint8)

    degree, endpoints = degree_map(skeleton_bin)

    def walk_path(
        start_pixel: Tuple[int, int],
        previous_pixel: Optional[Tuple[int, int]] = None,
    ) -> List[Tuple[int, int]]:
        """
        Walk along the skeleton starting from start_pixel until an endpoint/junction is reached.
        """
        path: List[Tuple[int, int]] = [start_pixel]
        current_row, current_col = start_pixel
        visited[current_row, current_col] = 1

        prev_row, prev_col = (previous_pixel if previous_pixel is not None else (None, None))

        while True:
            candidates: List[Tuple[int, int]] = []
            for neighbor_row, neighbor_col in neighbors(skeleton_bin, current_row, current_col):
                if visited[neighbor_row, neighbor_col] > 0:
                    continue
                if prev_row is not None and (neighbor_row, neighbor_col) == (prev_row, prev_col):
                    continue
                candidates.append((neighbor_row, neighbor_col))

            if not candidates:
                break

            if len(candidates) > 1:
                candidates.sort(key=lambda p: abs(2 - int(degree[p[0], p[1]])))

            next_row, next_col = candidates[0]
            prev_row, prev_col = current_row, current_col
            current_row, current_col = next_row, next_col

            visited[current_row, current_col] = 1
            path.append((current_row, current_col))

            if degree[current_row, current_col] != 2 and len(path) > 1:
                break

        return path

    paths: List[List[Tuple[int, int]]] = []

    for endpoint in endpoints:
        endpoint_row, endpoint_col = endpoint
        if visited[endpoint_row, endpoint_col] > 0:
            continue

        endpoint_path = walk_path(endpoint)
        if len(endpoint_path) >= 2:
            paths.append(endpoint_path)

    remaining_rows, remaining_cols = np.where((skeleton_bin > 0) & (visited == 0))
    for start_row, start_col in zip(remaining_rows, remaining_cols):
        if visited[start_row, start_col] > 0:
            continue

        start_neighbors = list(neighbors(skeleton_bin, start_row, start_col))
        if not start_neighbors:
            visited[start_row, start_col] = 1
            continue

        forward_path = walk_path((start_row, start_col), previous_pixel=start_neighbors[0])

        visited[start_row, start_col] = 0
        second_prev = forward_path[1] if len(forward_path) > 1 else None
        backward_path = walk_path((start_row, start_col), previous_pixel=second_prev)

        if len(forward_path) > 1 or len(backward_path) > 1:
            backward_reversed = backward_path[::-1]
            if backward_reversed and backward_reversed[-1] == (start_row, start_col):
                backward_reversed = backward_reversed[:-1]
            paths.append(backward_reversed + forward_path)

    return paths

def rdp(points: List[Tuple[int, int]], eps: float = RDP_EPS) -> List[Tuple[int, int]]:
    """
    Ramer-Douglas-Peucker simplification for a polyline.

    Args:
        points (list[tuple[int, int]]): Path points as (row, col).
        eps (float): Simplification threshold (pixels).

    Returns:
        list[tuple[int, int]]: Simplified path.
    """
    if len(points) < 3:
        return points

    # Convert (row, col) -> (x, y) for geometric computation.
    pts_xy = np.array(points, dtype=np.float32)[:, ::-1]
    start_xy = pts_xy[0]
    end_xy = pts_xy[-1]

    segment_vec = end_xy - start_xy
    segment_len = float(np.linalg.norm(segment_vec))
    if segment_len == 0.0:
        return [points[0], points[-1]]

    # Perpendicular distance to the start-end line using cross product magnitude.
    rel_vec = pts_xy - start_xy
    dist = np.abs(segment_vec[0] * rel_vec[:, 1] - segment_vec[1] * rel_vec[:, 0]) / segment_len

    split_idx = int(np.argmax(dist))
    max_dist = float(dist[split_idx])

    if max_dist > eps:
        left = rdp(points[: split_idx + 1], eps)
        right = rdp(points[split_idx:], eps)
        return left[:-1] + right

    return [points[0], points[-1]]

def segments_from_path_snap45(path_pts, rdp_eps: float = RDP_EPS, min_pixels: int = MIN_SEG_PX):
    """
    Convert a traced path into snapped line segments.

    Process:
        - Simplify the path with RDP.
        - Estimate edge directions and snap them to 45-degree increments.
        - Merge consecutive edges with the same snapped direction into segment records.

    Args:
        path_pts (list[tuple[int, int]]): Path points as (row, col).
        rdp_eps (float): RDP epsilon.
        min_pixels (int): Minimum segment length in pixels.

    Returns:
        list[dict]: Segment records with {phi, t, n, s_min, s_max, u0}.
    """
    if len(path_pts) < 2:
        return []

    simplified = rdp(path_pts, rdp_eps)
    points_xy = np.array(simplified, dtype=np.float32)[:, ::-1]  # (x, y)

    edge_angles = []
    for edge_idx in range(len(points_xy) - 1):
        vec = points_xy[edge_idx + 1] - points_xy[edge_idx]
        if np.linalg.norm(vec) < 1e-6:
            edge_angles.append(None)
            continue

        angle_deg = (math.degrees(math.atan2(vec[1], vec[0])) + 180.0) % 180.0
        edge_angles.append(snap45(angle_deg))

    segments = []
    edge_idx = 0

    while edge_idx < len(edge_angles):
        if edge_angles[edge_idx] is None:
            edge_idx += 1
            continue

        phi = edge_angles[edge_idx]
        end_idx = edge_idx
        while end_idx + 1 < len(edge_angles) and edge_angles[end_idx + 1] == phi:
            end_idx += 1

        group_pts = points_xy[edge_idx : end_idx + 2]
        if len(group_pts) >= 2:
            rad = math.radians(phi)
            t = np.array([math.cos(rad), math.sin(rad)], np.float32)
            n = np.array([-t[1], t[0]], np.float32)

            s = group_pts @ t
            u = group_pts @ n

            u0 = float(np.median(u))
            s_min = float(np.min(s))
            s_max = float(np.max(s))

            if (s_max - s_min) >= min_pixels:
                segments.append(dict(phi=phi, t=t, n=n, s_min=s_min, s_max=s_max, u0=u0))

        edge_idx = end_idx + 1

    return segments

def merge_collinear(segs, junctions=[], merge_gap: float = MERGE_GAP_PX, perp_tol_px: float = PERP_TOL_PX, max_iter: int = 6):
    """
    Merge collinear (or near-collinear) segments.

    Segments are merged if:
        - orientation matches (phi, modulo 180),
        - distance to the supporting line is within perp_tol_px,
        - projections along the segment axis overlap or are close within merge_gap,
        - no junction point lies inside the merged interval (prevents merging across junctions).

    Args:
        segs (list[dict]): Segment records.
        junctions (list[tuple[float, float]]): Junction points (x, y).
        merge_gap (float): Allowed projection gap along the segment axis.
        perp_tol_px (float): Max perpendicular distance for merge.
        max_iter (int): Max merge iterations.

    Returns:
        list[dict]: Merged segments.
    """
    junc_arr = np.array(junctions, dtype=np.float32) if len(junctions) > 0 else None

    changed = True
    iteration = 0

    while changed and iteration < max_iter:
        iteration += 1
        changed = False

        merged = []
        used = [False] * len(segs)

        for base_idx, base_seg in enumerate(segs):
            if used[base_idx]:
                continue

            phi = base_seg["phi"]
            t = base_seg["t"]
            n = base_seg["n"]
            u0 = base_seg["u0"]
            s_min = base_seg["s_min"]
            s_max = base_seg["s_max"]

            used[base_idx] = True

            for cand_idx, cand_seg in enumerate(segs):
                if cand_idx == base_idx or used[cand_idx]:
                    continue

                angle_diff = abs((cand_seg["phi"] - phi + 90.0) % 180.0 - 90.0)
                if angle_diff > 1e-3:
                    continue

                if abs(cand_seg["u0"] - u0) > perp_tol_px:
                    continue

                if cand_seg["s_max"] < s_min - merge_gap or cand_seg["s_min"] > s_max + merge_gap:
                    continue

                new_s_min = min(s_min, cand_seg["s_min"])
                new_s_max = max(s_max, cand_seg["s_max"])

                if junc_arr is not None:
                    j_s = junc_arr @ t
                    j_u = junc_arr @ n
                    on_line = np.abs(j_u - u0) < (perp_tol_px + 2.0)
                    if np.any(on_line):
                        split_margin = max(2.0, merge_gap * 0.5)
                        inside = (j_s > (new_s_min + split_margin)) & (j_s < (new_s_max - split_margin))
                        if np.any(on_line & inside):
                            continue

                s_min = new_s_min
                s_max = new_s_max
                used[cand_idx] = True
                changed = True

            merged.append(dict(phi=phi, t=t, n=n, s_min=float(s_min), s_max=float(s_max), u0=float(u0)))

        segs = merged

    return segs

def segment_to_rect(seg, dist_img, junctions=[], clamp=(THICK_MIN_PX, THICK_MAX_PX)):
    """
    Convert a line segment record into a rectangle (quad) representing a wall.

    Thickness is estimated from the distance transform along the segment.

    Args:
        seg (dict): Segment record.
        dist_img (np.ndarray): Distance transform (H, W), float32.
        junctions (list[tuple[float, float]]): Junction points (x, y).
        clamp (tuple[float, float]): Min/max thickness in pixels.

    Returns:
        list[tuple[float, float]] | None: 4 rectangle vertices (x, y) or None.
    """
    t = seg["t"]
    n = seg["n"]
    s_min = seg["s_min"]
    s_max = seg["s_max"]
    u0 = seg["u0"]

    segment_length = float(s_max - s_min)
    if segment_length < 1e-3:
        return None

    height, width = dist_img.shape

    sample_count = max(10, int(segment_length / 3.0))
    sample_s = np.linspace(s_min, s_max, num=sample_count)

    sample_pts = np.stack([t[0] * sample_s + n[0] * u0, t[1] * sample_s + n[1] * u0], axis=1)
    sample_cols = np.clip(np.round(sample_pts[:, 0]).astype(int), 0, width - 1)
    sample_rows = np.clip(np.round(sample_pts[:, 1]).astype(int), 0, height - 1)

    sample_vals = dist_img[sample_rows, sample_cols]

    thickness = float(np.median(sample_vals) * 2.0)
    thickness = max(clamp[0], min(thickness, clamp[1]))

    # If endpoints are close to a junction, extend a bit to avoid small gaps.
    if len(junctions) > 0:
        junction_arr = np.array(junctions, dtype=np.float32)
        start_xy = np.array([t[0] * s_min + n[0] * u0, t[1] * s_min + n[1] * u0], dtype=np.float32)
        end_xy = np.array([t[0] * s_max + n[0] * u0, t[1] * s_max + n[1] * u0], dtype=np.float32)

        dist_thr = 5.0

        dist_start = float(np.min(np.linalg.norm(junction_arr - start_xy, axis=1)))
        if dist_start < dist_thr:
            s_min -= thickness * 0.5

        dist_end = float(np.min(np.linalg.norm(junction_arr - end_xy, axis=1)))
        if dist_end < dist_thr:
            s_max += thickness * 0.5

    half = thickness / 2.0

    p0 = [t[0] * s_min + n[0] * (u0 + half), t[1] * s_min + n[1] * (u0 + half)]
    p1 = [t[0] * s_max + n[0] * (u0 + half), t[1] * s_max + n[1] * (u0 + half)]
    p2 = [t[0] * s_max + n[0] * (u0 - half), t[1] * s_max + n[1] * (u0 - half)]
    p3 = [t[0] * s_min + n[0] * (u0 - half), t[1] * s_min + n[1] * (u0 - half)]

    return [
        (float(p0[0]), float(p0[1])),
        (float(p1[0]), float(p1[1])),
        (float(p2[0]), float(p2[1])),
        (float(p3[0]), float(p3[1])),
    ]

def get_junction_points(skbin: np.ndarray) -> List[Tuple[int, int]]:
    """
    Extract junction pixels (degree > 2) from a skeleton binary image.

    Downstream code expects (x, y) ordering for junction points.
    """
    degree, _ = degree_map(skbin)
    junction_rows, junction_cols = np.where(degree > 2)
    return list(zip(junction_cols, junction_rows))

def components_to_opening_segs(mask, snap: bool = True, min_area: float = 40):
    """
    Extract oriented opening segments from a binary mask (door/window components).

    Each connected component is approximated by a minimum-area rectangle.

    Args:
        mask (np.ndarray): Binary mask (H, W), uint8.
        snap (bool): If True, snap orientation to 45 degrees.
        min_area (float): Minimum component area to keep.

    Returns:
        list[dict]: Opening segment records with {phi, t, n, s_min, s_max, u0, thickness}.
    """
    segments = []
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    for contour in contours:
        if cv2.contourArea(contour) < min_area:
            continue

        (center_x, center_y), (rect_w, rect_h), angle_deg = cv2.minAreaRect(contour)

        # Normalize so that rect_w is the major axis.
        if rect_w < rect_h:
            rect_w, rect_h = rect_h, rect_w
            angle_deg += 90.0

        phi = (angle_deg + 360.0) % 180.0
        if snap:
            phi = snap45(phi)

        rad = math.radians(phi)
        t = np.array([math.cos(rad), math.sin(rad)], np.float32)
        n = np.array([-t[1], t[0]], np.float32)

        s0 = center_x * t[0] + center_y * t[1] - rect_w / 2.0
        s1 = s0 + rect_w
        u0 = center_x * n[0] + center_y * n[1]

        segments.append(
            dict(
                phi=float(phi),
                t=t,
                n=n,
                s_min=float(min(s0, s1)),
                s_max=float(max(s0, s1)),
                u0=float(u0),
                thickness=float(rect_h),
            )
        )

    return segments

def seg_to_rect_points(seg):
    L = seg["s_max"]-seg["s_min"]
    sc=0.5*(seg["s_min"]+seg["s_max"]); u0=seg["u0"]; T=seg["thickness"]
    r=math.radians(seg["phi"])
    t=np.array([math.cos(r), math.sin(r)],np.float32)
    n=np.array([-t[1], t[0]],np.float32)
    cx=t[0]*sc + n[0]*u0; cy=t[1]*sc + n[1]*u0
    hl, ht = L/2.0, T/2.0
    c0=[cx+ t[0]*hl + n[0]*ht, cy+ t[1]*hl + n[1]*ht]
    c1=[cx- t[0]*hl + n[0]*ht, cy- t[1]*hl + n[1]*ht]
    c2=[cx- t[0]*hl - n[0]*ht, cy- t[1]*hl - n[1]*ht]
    c3=[cx+ t[0]*hl - n[0]*ht, cy+ t[1]*hl - n[1]*ht]
    return [(float(c0[0]),float(c0[1])),(float(c1[0]),float(c1[1])),
            (float(c2[0]),float(c2[1])),(float(c3[0]),float(c3[1]))]

# ======================================================= #
#                    Polygon / mask helpers
# ======================================================= #
def area(poly: List[Tuple[float,float]]) -> float:
    a=0.0
    for i in range(len(poly)):
        x1,y1=poly[i]; x2,y2=poly[(i+1)%len(poly)]
        a += x1*y2 - x2*y1
    return 0.5*a

def ensure_ccw(poly):
    """
    Ensure polygon winding is counter-clockwise (CCW).
    """
    if area(poly) > 0:
        return poly
    return list(reversed(poly))

def polygon_to_mask(poly_xy, hw):
    """
    Rasterize a polygon into a binary mask.
    """
    height, width = hw
    mask = np.zeros((height, width), dtype=np.uint8)
    pts = np.array(poly_xy, dtype=np.int32).reshape(-1, 1, 2)
    cv2.fillPoly(mask, [pts], 255)
    return mask

def walls_to_mask(walls_xy, hw):
    """
    Rasterize multiple polygons into a single binary mask.
    """
    mask = np.zeros(hw, dtype=np.uint8)
    for wall_poly in walls_xy:
        pts = np.array(wall_poly, dtype=np.int32).reshape(-1, 1, 2)
        cv2.fillPoly(mask, [pts], 255)
    return mask

def assign_openings_to_walls_raster(opening_rects_xy, wall_rects_xy, hw, dilate_px: int = 2):
    """
    Assign each opening rectangle to the most likely wall by raster overlap.

    Args:
        opening_rects_xy (list[list[tuple[float,float]]]): Opening quads (x, y).
        wall_rects_xy (list[list[tuple[float,float]]]): Wall quads (x, y).
        hw (tuple[int, int]): Canvas size (H, W).
        dilate_px (int): Wall dilation radius (pixels) for robust overlap.

    Returns:
        list[int]: wall indices for each opening, aligned with opening_rects_xy.
    """
    height, width = hw

    wall_masks = []
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (2 * dilate_px + 1, 2 * dilate_px + 1))

    for wall_poly in wall_rects_xy:
        wall_mask = polygon_to_mask(wall_poly, (height, width))
        wall_mask = cv2.dilate(wall_mask, kernel, iterations=1)
        wall_masks.append(wall_mask)

    assigned_ids = []
    for opening_poly in opening_rects_xy:
        opening_mask = polygon_to_mask(opening_poly, (height, width))

        best_wall_idx = -1
        best_overlap = 0

        for wall_idx, wall_mask in enumerate(wall_masks):
            overlap = int(cv2.countNonZero(cv2.bitwise_and(opening_mask, wall_mask)))
            if overlap > best_overlap:
                best_overlap = overlap
                best_wall_idx = wall_idx

        if best_wall_idx < 0:
            # Fallback: nearest wall center by centroid distance.
            opening_center_x = sum(pt[0] for pt in opening_poly) / 4.0
            opening_center_y = sum(pt[1] for pt in opening_poly) / 4.0

            wall_centers = []
            for wall_poly in wall_rects_xy:
                center_x = sum(pt[0] for pt in wall_poly) / 4.0
                center_y = sum(pt[1] for pt in wall_poly) / 4.0
                wall_centers.append((center_x, center_y))

            d2 = [
                (opening_center_x - cx) ** 2 + (opening_center_y - cy) ** 2
                for (cx, cy) in wall_centers
            ]
            best_wall_idx = int(np.argmin(d2))

        assigned_ids.append(best_wall_idx)

    return assigned_ids

def extract_floors_from_walls(walls_xy, hw, approx_eps=FLOOR_APPROX_EPS, min_area=FLOOR_MIN_AREA):
    """
    Extract interior floor polygons from wall rectangles.

    Method:
        - Rasterize and dilate walls.
        - Invert mask and flood-fill from outside to isolate interior region.
        - Contours of interior region become floor candidates.

    Args:
        walls_xy (list[list[tuple[float, float]]]): Wall rectangles (x, y) in pixels.
        hw (tuple[int, int]): Canvas size (H, W).
        approx_eps (float): approxPolyDP epsilon.
        min_area (float): Minimum area threshold.

    Returns:
        list[tuple[list[tuple[float,float]], list[int]]]: Floor polygons and triangle indices.
    """
    height, width = hw

    wall_mask = walls_to_mask(walls_xy, (height, width))

    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3))
    wall_mask_dilated = cv2.dilate(wall_mask, kernel, iterations=1)

    inv = cv2.bitwise_not(wall_mask_dilated)
    ff = inv.copy()
    flood_mask = np.zeros((height + 2, width + 2), np.uint8)
    cv2.floodFill(ff, flood_mask, (0, 0), 123)

    inside = (ff != 123) & (inv > 0)
    inside = inside.astype(np.uint8) * 255

    contours, _ = cv2.findContours(inside, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)

    floors = []
    for contour in contours:
        if cv2.contourArea(contour) < min_area:
            continue

        refined_eps = approx_eps * 0.5
        approx = cv2.approxPolyDP(contour, refined_eps, True)

        poly = [(float(p[0][0]), float(p[0][1])) for p in approx.reshape(-1, 1, 2)]
        poly = ensure_cw(poly)

        indices = []
        for k in range(1, len(poly) - 1):
            indices += [0, k, k + 1]

        floors.append((poly, indices))

    return floors

def map_room_walls(floor_poly, walls_xy, hw, touch_px=ADJ_TOUCH_THICK):
    """
    Map a floor polygon to adjacent wall IDs by band intersection.
    """
    height, width = hw
    floor_mask = polygon_to_mask(floor_poly, (height, width))

    band_width = 2 * touch_px + 1
    k = cv2.getStructuringElement(cv2.MORPH_RECT, (band_width, band_width))
    band = cv2.morphologyEx(floor_mask, cv2.MORPH_GRADIENT, k, iterations=1)

    wall_mask = walls_to_mask(walls_xy, (height, width))
    hit = cv2.bitwise_and(band, wall_mask)

    ids = []
    min_contact_ratio = 0.02

    for wall_idx, wall_poly in enumerate(walls_xy):
        wall_poly_mask = polygon_to_mask(wall_poly, (height, width))
        wall_area_px = int(cv2.countNonZero(wall_poly_mask))
        if wall_area_px == 0:
            continue

        intersect_px = int(cv2.countNonZero(cv2.bitwise_and(hit, wall_poly_mask)))
        if intersect_px <= 0:
            continue

        contact_ratio = float(intersect_px) / float(wall_area_px)
        if contact_ratio >= min_contact_ratio:
            ids.append(wall_idx)

    return ids

def add_room_memberships(out_json: dict) -> None:
    """
    Populate roomID memberships for walls, floors, and openings.

    Args:
        out_json (dict): Output JSON dictionary (modified in-place).

    Raises:
        ValueError: If roomID=-1 room does not exist.
    """
    rooms    = out_json.get("rooms", [])
    walls    = out_json.get("walls", [])
    floors   = out_json.get("floors", [])
    openings = out_json.get("openings", [])

    others = None
    for rm in rooms:
        rid = int(rm.get("roomID", rm.get("id", 0)))
        if rid == -1:
            others = rm
            break
    if others is None:
        raise ValueError("RoomID = -1 room must be created in main.")

    wall_to_rooms: dict[int, set[int]]  = {}
    floor_to_rooms: dict[int, set[int]] = {}

    for rm in rooms:
        rid = int(rm.get("roomID", rm.get("id", 0)))

        fids = rm.get("floorIDs", []) or []
        for fid in fids:
            fid = int(fid)
            floor_to_rooms.setdefault(fid, set()).add(rid)

        wids = rm.get("wallIDs", []) or []
        for wid in wids:
            wid = int(wid)
            wall_to_rooms.setdefault(wid, set()).add(rid)

    minus1_walls: list[int] = []
    for w in walls:
        wid = int(w["id"])
        rset = wall_to_rooms.get(wid, set())
        if rset:
            w["roomID"] = sorted(int(x) for x in rset)
        else:
            w["roomID"] = [-1]
            minus1_walls.append(wid)

    minus1_floors: list[int] = []
    for f in floors:
        fid = int(f["id"])
        rset = floor_to_rooms.get(fid, set())
        if rset:
            f["roomID"] = sorted(int(x) for x in rset)
        else:
            f["roomID"] = [-1]
            minus1_floors.append(fid)

    wall_room_map = {int(w["id"]): w.get("roomID", []) for w in walls}
    for op in openings:
        wid = int(op.get("wallID", -1))
        rids = wall_room_map.get(wid)
        op["roomID"] = list(rids) if rids else [-1]

    ow = set(int(x) for x in others.get("wallIDs", []) if x is not None)
    of = set(int(x) for x in others.get("floorIDs", []) if x is not None)
    ow.update(minus1_walls)
    of.update(minus1_floors)
    others["wallIDs"]  = sorted(ow)
    others["floorIDs"] = sorted(of)
    out_json["rooms"] = rooms

def _poly_area_xz(verts):
    """
    Compute polygon area in the XZ plane.

    Args:
        verts (list[dict]): Vertices with keys {"x","z"}.

    Returns:
        float: Area.
    """
    count = len(verts)
    if count < 3:
        return 0.0

    acc = 0.0
    for idx in range(count):
        x1 = float(verts[idx]["x"])
        z1 = float(verts[idx]["z"])
        x2 = float(verts[(idx + 1) % count]["x"])
        z2 = float(verts[(idx + 1) % count]["z"])
        acc += x1 * z2 - z1 * x2

    return abs(acc) * 0.5

def _poly_area_px(poly_px):
    pts = np.asarray(poly_px, dtype=np.float32)
    x = pts[:, 0]; y = pts[:, 1]
    return float(0.5 * abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1))))

def _tri_area_xz(pa, pb, pc):
    ax, az = float(pa["x"]), float(pa["z"])
    bx, bz = float(pb["x"]), float(pb["z"])
    cx, cz = float(pc["x"]), float(pc["z"])
    return 0.5 * abs((bx-ax)*(cz-az) - (bz-az)*(cx-ax))

def _floor_area(floor):
    vs  = floor.get("vertices", [])
    idx = floor.get("indices", [])
    if isinstance(idx, list) and len(idx) >= 3 and len(idx) % 3 == 0:
        area = 0.0
        for k in range(0, len(idx), 3):
            a, b, c = int(idx[k]), int(idx[k+1]), int(idx[k+2])
            area += _tri_area_xz(vs[a], vs[b], vs[c])
        return area
    return _poly_area_xz(vs)

def _total_floor_area(out_json):
    return sum(_floor_area(f) for f in out_json.get("floors", []))

def _collect_bbox_from_walls_floors(out_json):
    xs, zs = [], []
    for lst_key in ("walls", "floors"):
        for it in out_json.get(lst_key, []):
            for v in it.get("vertices", []):
                xs.append(float(v["x"]))
                zs.append(float(v["z"]))
    if not xs or not zs:
        return (0.0, 0.0, 0.0, 0.0)
    return (min(xs), max(xs), min(zs), max(zs))

def _shift_all(out_json, dx, dz):
    def _shift_vlist(vlist):
        for v in vlist:
            v["x"] = float(v["x"]) + float(dx)
            v["z"] = float(v["z"]) + float(dz)

    for it in out_json.get("walls", []):
        _shift_vlist(it.get("vertices", []))
    for it in out_json.get("floors", []):
        _shift_vlist(it.get("vertices", []))

    for op in out_json.get("openings", []):
        if "center" in op and op["center"] is not None:
            op["center"]["x"] = float(op["center"]["x"]) + float(dx)
            op["center"]["z"] = float(op["center"]["z"]) + float(dz)
        if "hingePos" in op and op["hingePos"] is not None:
            op["hingePos"]["x"] = float(op["hingePos"]["x"]) + float(dx)
            op["hingePos"]["z"] = float(op["hingePos"]["z"]) + float(dz)
        if "vertices" in op and isinstance(op["vertices"], list):
            _shift_vlist(op["vertices"])

def _scale_all(out_json, s):
    def _scale_vlist(vlist):
        for v in vlist:
            v["x"] = float(v["x"]) * float(s)
            v["z"] = float(v["z"]) * float(s)

    for it in out_json.get("walls", []):
        _scale_vlist(it.get("vertices", []))
    for it in out_json.get("floors", []):
        _scale_vlist(it.get("vertices", []))

    for op in out_json.get("openings", []):
        if "center" in op and op["center"] is not None:
            op["center"]["x"] = float(op["center"]["x"]) * float(s)
            op["center"]["z"] = float(op["center"]["z"]) * float(s)
        if "hingePos" in op and op["hingePos"] is not None:
            op["hingePos"]["x"] = float(op["hingePos"]["x"]) * float(s)
            op["hingePos"]["z"] = float(op["hingePos"]["z"]) * float(s)
        if "vertices" in op and isinstance(op["vertices"], list):
            _scale_vlist(op["vertices"])
        if "width" in op and op["width"] is not None:
            op["width"] = float(op["width"]) * float(s)

def recenter_to_origin(out_json):
    """
    Shift all vertices so that the bounding box center becomes the origin.

    Args:
        out_json (dict): Output JSON dictionary (modified in-place).

    Returns:
        tuple[float, float]: (cx, cz) center that was subtracted.
    """
    xmin, xmax, zmin, zmax = _collect_bbox_from_walls_floors(out_json)
    cx = 0.5 * (xmin + xmax)
    cz = 0.5 * (zmin + zmax)
    _shift_all(out_json, dx=-cx, dz=-cz)
    return (cx, cz)

def scale_by_pyeong(out_json, target_pyeong):
    """
    Scale all geometry so total floor area matches the target size in pyeong.

    Args:
        out_json (dict): Output JSON dictionary (modified in-place).
        target_pyeong (float): Target area size in pyeong.

    Returns:
        float: Applied uniform scale factor.
    """
    A_curr = _total_floor_area(out_json)
    if A_curr <= 0:
        return 1.0
    A_tar  = float(target_pyeong) * _PYEONG_TO_M2
    s = math.sqrt(max(A_tar, 1e-12) / A_curr)
    _scale_all(out_json, s)
    return s

def compute_exterior_walls(wall_rects_xy, canvas_hw, band_px=3):
    """
    Determine whether each wall touches the exterior.

    Args:
        wall_rects_xy (list[list[tuple[float, float]]]): Wall rectangles in pixels.
        canvas_hw (tuple[int, int]): Canvas size (H, W).
        band_px (int): Rim band thickness in pixels.

    Returns:
        list[bool]: Exterior flag per wall index.
    """
    H, W = canvas_hw
    if H <= 0 or W <= 0 or not wall_rects_xy:
        return [False]*len(wall_rects_xy)

    wall_mask = np.zeros((H, W), np.uint8)
    polys = []
    for poly in wall_rects_xy:
        p = np.array(poly, np.int32)
        polys.append(p)
        cv2.fillPoly(wall_mask, [p], 255)

    space = np.where(wall_mask == 0, 255, 0).astype(np.uint8)
    ff = space.copy()
    mask = np.zeros((H+2, W+2), np.uint8)
    for seed in [(0,0), (W-1,0), (0,H-1), (W-1,H-1)]:
        if ff[seed[1], seed[0]] == 255:
            cv2.floodFill(ff, mask, seedPoint=seed, newVal=128)
    outside_mask = (ff == 128).astype(np.uint8) * 255

    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (2*band_px+1, 2*band_px+1))
    flags = []
    for p in polys:
        wm = np.zeros_like(wall_mask)
        cv2.fillPoly(wm, [p], 255)
        rim = cv2.dilate(wm, kernel, 1)
        rim = cv2.subtract(rim, wm)
        touching = cv2.countNonZero(cv2.bitwise_and(rim, outside_mask)) > 0
        flags.append(bool(touching))
    return flags

# ======================================================= #
#                    Main
# ======================================================= #
def main():
    """
    CLI entry point.

    Arguments:
        --input         Input color-coded floor plan image.
        --output        Output JSON file path.
        --target_pyeong Optional target area (pyeong) to scale the geometry.

    Raises:
        FileNotFoundError: If the input image cannot be loaded.
    """
    p = argparse.ArgumentParser(description="Convert Floor Plan Image to Space JSON")
    p.add_argument("--input", required=True, help="Input image file path")
    p.add_argument("--output", required=True, help="Output JSON file path")
    p.add_argument("--target_pyeong", type=float, required=False, help="Target area size in Pyeong")
    args = p.parse_args()

    in_path = Path(args.input)
    out_path = Path(args.output)
    
    # Ensure output directory exists
    out_path.parent.mkdir(parents=True, exist_ok=True)

    img = cv2.imread(str(in_path), cv2.IMREAD_COLOR)
    if img is None: 
        raise FileNotFoundError(f"Could not load image: {in_path}")
    H,W = img.shape[:2]

    # ---------- 1. 벽 마스크 생성 (메모리 내 문 클리핑) ----------
    img_clipped = clip_doors_auto_kernel(img)
    
    gray = cv2.cvtColor(img_clipped, cv2.COLOR_BGR2GRAY)
    wall_bin = (gray > 0).astype(np.uint8) * 255
    k = cv2.getStructuringElement(cv2.MORPH_RECT, (3,3))
    wall_bin = cv2.morphologyEx(wall_bin, cv2.MORPH_OPEN, k, 1)
    wall_bin = cv2.morphologyEx(wall_bin, cv2.MORPH_CLOSE, k, 1)

    dist = cv2.distanceTransform((wall_bin > 0).astype(np.uint8), cv2.DIST_L2, 5)
    door_mask = mask_color(img, "green")
    sk = thinning(wall_bin, door_mask=door_mask)
    junction_points = get_junction_points((sk > 0).astype(np.uint8))
    paths = trace_paths(sk)

    segs = []
    for pth in paths:
        segs += segments_from_path_snap45(pth, RDP_EPS, MIN_SEG_PX)
    
    segs = merge_collinear(segs, junctions=junction_points, merge_gap=MERGE_GAP_PX, perp_tol_px=PERP_TOL_PX, max_iter=6)
    
    wall_rects_xy = []
    for s in segs:
        rxy = segment_to_rect(s, dist, junctions=junction_points, clamp=(THICK_MIN_PX, THICK_MAX_PX))
        if rxy is not None: 
            wall_rects_xy.append(order_ccw_vertices_xy(rxy))
    
    # ---------- openings ----------
    mG = mask_color(img, "green")
    mR = mask_color(img, "red")
    door_segs = components_to_opening_segs(mG, True, 40)
    win_segs  = components_to_opening_segs(mR, True, 40)

    opening_types = (["door"]*len(door_segs)) + (["window"]*len(win_segs))
    opening_rects_xy = [order_ccw_vertices_xy(seg_to_rect_points(s)) for s in door_segs] + \
                       [order_ccw_vertices_xy(seg_to_rect_points(s)) for s in win_segs]
    
    wall_ids = assign_openings_to_walls_raster(opening_rects_xy, wall_rects_xy, (H,W), dilate_px=2)
    floors_infos = extract_floors_from_walls(wall_rects_xy, (H,W), approx_eps=FLOOR_APPROX_EPS, min_area=FLOOR_MIN_AREA)

    # ---------- JSON 구성 ----------
    out_json = {
        "metersPerPixel": M_PER_PX,
        "ceilingHeight": CEILING_H,
        "doorHeight": DOOR_H,
        "windowHeight": WINDOW_H,
        "windowY": WINDOW_Y,
        "floors": [],
        "walls": [],
        "openings": [],
        "rooms": []
    }

    for i, vxy in enumerate(wall_rects_xy):
        out_json["walls"].append({
            "id": i,
            "vertices": xy_to_m_rect(vxy),
            "indices": [0,1,2, 0,2,3]
        })

    opening_infos = []
    door_widths = []
    for typ, vxy, wid in zip(opening_types, opening_rects_xy, wall_ids):
        verts_m = xy_to_m_rect(vxy)
        side0 = math.hypot(verts_m[0]["x"]-verts_m[1]["x"], verts_m[0]["z"]-verts_m[1]["z"])
        side1 = math.hypot(verts_m[1]["x"]-verts_m[2]["x"], verts_m[1]["z"]-verts_m[2]["z"])
        width = max(side0, side1)
        cx = sum(v["x"] for v in verts_m) / 4.0
        cz = sum(v["z"] for v in verts_m) / 4.0
        opening_infos.append((typ, verts_m, wid, cx, cz, width))
        if typ == "door":
            door_widths.append(width)

    width_thr = 0.0
    if len(door_widths) > 0:
        width_thr = (sum(door_widths) / float(len(door_widths))) / 3.0

    oid = 0
    for typ, verts_m, wid, cx, cz, width in opening_infos:
        if typ == "door" and width_thr > 0.0 and width < width_thr:
            continue

        if typ == "door" and 0 <= wid < len(out_json["walls"]):
            wall_verts = out_json["walls"][wid]["vertices"]
            p0 = wall_verts[0]
            p1 = wall_verts[1]
            p3 = wall_verts[3]

            e1x = p1["x"] - p0["x"]
            e1z = p1["z"] - p0["z"]
            e3x = p3["x"] - p0["x"]
            e3z = p3["z"] - p0["z"]

            len1 = math.hypot(e1x, e1z)
            len3 = math.hypot(e3x, e3z)

            if len1 >= len3 and len1 > 1e-6:
                ux, uz = e1x / len1, e1z / len1
            elif len3 > 1e-6:
                ux, uz = e3x / len3, e3z / len3
            else:
                ux, uz = 1.0, 0.0

            hx = cx - ux * (width * 0.5)
            hz = cz - uz * (width * 0.5)
        else:
            hx, hz = cx, cz

        opening_obj = {
            "id": oid,
            "type": typ,
            "wallID": int(wid),
            "center": {"x": cx, "y": 0.0, "z": cz},
            "hingePos": {"x": hx, "y": 0.0, "z": hz},
            "width": float(width),
            "isCW": False
        }
        out_json["openings"].append(opening_obj)
        oid += 1

    for i,(poly_px, idxs) in enumerate(floors_infos):
        out_json["floors"].append({
            "id": i,
            "vertices": xy_to_m_rect(poly_px),
            "indices": idxs
        })

    # Rooms
    out_json["rooms"].append({ "roomID": -1, "name": f"Others", "floorIDs": [],"wallIDs": [] })
    others_idx = len(out_json["rooms"]) - 1
    
    areas_px = []
    for (poly_px, _) in floors_infos:
        areas_px.append(_poly_area_px(poly_px))
    total_area_px = sum(areas_px) if areas_px else 1.0

    # Collect walls that have at least one opening.
    walls_with_open = set()
    for op in out_json.get("openings", []):
        if not isinstance(op, dict):
            continue
        try:
            wid = int(op.get("wallID", -1))
        except Exception:
            wid = -1
        if wid >= 0:
            walls_with_open.add(wid)
    
    roomid = 0
    for i,(poly_px,_) in enumerate(floors_infos):
        adj_walls = map_room_walls(poly_px, wall_rects_xy, (H,W))

        has_opening_on_walls = any((int(w) in walls_with_open) for w in adj_walls)
        area_ratio = (areas_px[i] / total_area_px) if total_area_px > 0 else 0.0
        small_enough = (area_ratio <= SMALL_ROOM_RATIO)

        if (not has_opening_on_walls) and small_enough:
            lst = out_json["rooms"][others_idx].get("floorIDs", [])
            if lst is None:
                lst = []
            lst = list({*map(int, lst), int(i)})
            out_json["rooms"][others_idx]["floorIDs"] = sorted(lst)
            continue

        out_json["rooms"].append({
            "roomID": roomid,
            "name": f"room_{roomid}",
            "floorIDs": [int(i)],
            "wallIDs": adj_walls
        })
        roomid += 1

    exterior_flags = compute_exterior_walls(wall_rects_xy, (H, W), band_px=3)
    
    for op in out_json["openings"]: 
        if op.get("type") == "window":
            wid = int(op.get("wallID", -1))
            if 0 <= wid < len(exterior_flags) and (not exterior_flags[wid]):
                op["type"] = "slidedoor"

    add_room_memberships(out_json)

    recenter_to_origin(out_json)

    if args.target_pyeong is not None:
        scale_by_pyeong(out_json, args.target_pyeong)
    
    out_path.write_text(json.dumps(out_json, ensure_ascii=False, indent=2), encoding="utf-8")
    print(f"[OK] wrote: {out_path}")

if __name__ == "__main__":
    main()