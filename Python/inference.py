# ===== import ===== #
import os

# Limit internal parallelism to keep CPU usage stable on Windows.
os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("MKL_NUM_THREADS", "1")

import argparse
from typing import Dict, Iterator, Tuple, Optional

import cv2
import numpy as np
import torch
import segmentation_models_pytorch as smp

# Limit OpenCV's internal threads (avoid "num_workers x cv threads" explosion).
cv2.setNumThreads(0)

# Inference can use faster backends. Disable if you need strict reproducibility.
torch.backends.cuda.matmul.allow_tf32 = True
torch.backends.cudnn.allow_tf32 = True
torch.backends.cudnn.benchmark = True

# ======================================================= #
#                 Fixed paths / settings                  #
# ======================================================= #

CKPTS = {
    "wall": "./wall_model.pth",
    "window": "./window_model.pth",
    "door_swing": "./swing_door_model.pth",
    "door_other": "../ckpt_bin/door_other/best.pth",
    "door_sliding": "../ckpt_bin/door_sliding/best.pth",
}

# Tile settings (keep consistent with training where applicable).
CORE_TILE = 1024
CONTEXT = 128

# Inference settings
USE_AMP = True
THRESH = 0.5

# Class IDs (must match train.py).
CLASS_ID = {
    "bg": 0,
    "wall": 1,
    "window": 2,
    "door_other": 3,
    "door_swing": 4,
    "door_sliding": 5,
}

# Overwrite priority (low -> high). Later classes overwrite earlier ones.
PRIORITY = ["wall", "window", "door_other", "door_sliding", "door_swing"]

# Color palette (BGR for OpenCV) indexed by CLASS_ID.
# NOTE: This is for visualization only; the actual ID map is stored separately.
PALETTE = np.array(
    [
        [0, 0, 0],        # 0 bg
        [255, 255, 255],  # 1 wall
        [0, 255, 0],      # 2 window
        [0, 255, 255],    # 3 door_other
        [0, 0, 255],      # 4 door_swing
        [255, 0, 0],      # 5 door_sliding
    ],
    dtype=np.uint8,
)

# ======================================================= #
#                       Utils                             #
# ======================================================= #

def id_to_color(mask_id: np.ndarray) -> np.ndarray:
    """
    Convert an ID mask (H, W) into a BGR color image (H, W, 3) using PALETTE.

    Args:
        mask_id (np.ndarray): ID mask of shape (H, W), dtype uint8.

    Returns:
        np.ndarray: Color image of shape (H, W, 3), dtype uint8 (BGR).
    """
    return PALETTE[mask_id]


# ------------------------------------------------------ #

def overlay(img_bgr: np.ndarray, mask_bgr: np.ndarray, alpha: float = 0.45) -> np.ndarray:
    """
    Alpha-blend a color mask over the original image.

    Args:
        img_bgr (np.ndarray): Source image (H, W, 3), dtype uint8.
        mask_bgr (np.ndarray): Color mask (H, W, 3), dtype uint8.
        alpha (float): Blend ratio for mask.

    Returns:
        np.ndarray: Overlay image (H, W, 3), dtype uint8.
    """
    out = img_bgr.copy()
    cv2.addWeighted(mask_bgr, alpha, out, 1.0 - alpha, 0, out)
    return out


# ------------------------------------------------------ #

def load_bin_model(ckpt_path: str, device: torch.device) -> torch.nn.Module:
    """
    Load a binary DeepLabV3Plus model checkpoint.

    The checkpoint format is expected to be either:
        - a dict containing "model" state_dict (train.py format), or
        - a dict containing "state_dict", or
        - a raw state_dict.

    Args:
        ckpt_path (str): Checkpoint path.
        device (torch.device): Target device.

    Returns:
        torch.nn.Module: Loaded model in eval() mode.
    """
    model = smp.DeepLabV3Plus(
        encoder_name="resnet34",
        encoder_weights=None,
        in_channels=3,
        classes=1,
        activation=None,
    )

    sd = torch.load(ckpt_path, map_location="cpu")
    if isinstance(sd, dict):
        state = sd.get("model", sd.get("state_dict", sd))
    else:
        state = sd

    model.load_state_dict(state, strict=True)
    model.eval().to(device)

    # Optional memory format optimization for CUDA inference.
    if device.type == "cuda":
        model = model.to(memory_format=torch.channels_last)

    return model


# ------------------------------------------------------ #

def tiles_iter(H: int, W: int, core: int) -> Iterator[Tuple[int, int, int, int]]:
    """
    Generate non-overlapping tile coordinates that cover the full image.

    Args:
        H (int): Image height.
        W (int): Image width.
        core (int): Core tile size.

    Yields:
        (y0, y1, x0, x1): Coordinates for each core tile.
    """
    ys = [0] if H <= core else list(range(0, H - core + 1, core))
    xs = [0] if W <= core else list(range(0, W - core + 1, core))

    # Ensure last tile reaches boundary.
    if ys[-1] != max(0, H - core):
        ys.append(max(0, H - core))
    if xs[-1] != max(0, W - core):
        xs.append(max(0, W - core))

    for y0 in ys:
        for x0 in xs:
            y1 = min(y0 + core, H)
            x1 = min(x0 + core, W)
            yield y0, y1, x0, x1


# ------------------------------------------------------ #

def cut_with_context(
    img_bgr: np.ndarray,
    y0: int,
    y1: int,
    x0: int,
    x1: int,
    ctx: int,
) -> Tuple[np.ndarray, int, int]:
    """
    Extract a context-aware patch for a given core tile.

    The extracted patch has size:
        (core_h + 2*ctx, core_w + 2*ctx)
    with padding applied when the context window crosses image boundaries.

    Args:
        img_bgr (np.ndarray): Input image (H, W, 3), dtype uint8.
        y0, y1, x0, x1 (int): Core tile coordinates.
        ctx (int): Context margin.

    Returns:
        patch (np.ndarray): Context-aware patch (need_h, need_w, 3), dtype uint8.
        core_h (int): Core height (y1-y0).
        core_w (int): Core width  (x1-x0).
    """
    H, W = img_bgr.shape[:2]
    core_h, core_w = y1 - y0, x1 - x0

    top, left = y0 - ctx, x0 - ctx
    bottom, right = y1 + ctx, x1 + ctx

    py0, px0 = max(0, top), max(0, left)
    py1, px1 = min(H, bottom), min(W, right)

    patch = img_bgr[py0:py1, px0:px1]
    need_h, need_w = core_h + 2 * ctx, core_w + 2 * ctx

    pad_top, pad_left = max(0, -top), max(0, -left)
    pad_bottom, pad_right = max(0, bottom - H), max(0, right - W)

    if pad_top or pad_bottom or pad_left or pad_right:
        patch = cv2.copyMakeBorder(
            patch,
            pad_top,
            pad_bottom,
            pad_left,
            pad_right,
            borderType=cv2.BORDER_CONSTANT,
            value=(0, 0, 0),
        )

    # Safety: ensure exact size.
    if patch.shape[0] != need_h or patch.shape[1] != need_w:
        patch = cv2.copyMakeBorder(
            patch,
            0,
            max(0, need_h - patch.shape[0]),
            0,
            max(0, need_w - patch.shape[1]),
            borderType=cv2.BORDER_CONSTANT,
            value=(0, 0, 0),
        )
        patch = patch[:need_h, :need_w]

    return patch, core_h, core_w


# ======================================================= #
#                      Inference                          #
# ======================================================= #

@torch.inference_mode()
def infer_one(models: Dict[str, torch.nn.Module], img_bgr: np.ndarray, device: torch.device) -> np.ndarray:
    """
    Compose per-class binary predictions into a final ID mask.

    For each class in PRIORITY:
        - Run tiled inference to build pred_bin (H, W) in {0,1}
        - Overwrite out[pred_bin == 1] = CLASS_ID[class_name]

    Args:
        models (dict): Dict[class_name -> model].
        img_bgr (np.ndarray): Input image (H, W, 3), dtype uint8.
        device (torch.device): Target device.

    Returns:
        np.ndarray: Final ID mask (H, W), dtype uint8, values in {0..5}.
    """
    H, W = img_bgr.shape[:2]
    out = np.zeros((H, W), dtype=np.uint8)

    for name in PRIORITY:
        if name not in models:
            continue

        cls_id = CLASS_ID.get(name, None)
        if cls_id is None:
            continue

        pred_bin = np.zeros((H, W), dtype=np.uint8)
        model = models[name]

        for y0, y1, x0, x1 in tiles_iter(H, W, CORE_TILE):
            patch, ch, cw = cut_with_context(img_bgr, y0, y1, x0, x1, CONTEXT)

            x = torch.from_numpy((patch.astype(np.float32) / 255.0).transpose(2, 0, 1)[None]).to(device)
            if device.type == "cuda":
                x = x.contiguous(memory_format=torch.channels_last)

            with torch.amp.autocast("cuda", enabled=(device.type == "cuda" and USE_AMP)):
                logit = model(x)[0, 0]  # [need_h, need_w]

            prob = torch.sigmoid(logit)[CONTEXT:CONTEXT + ch, CONTEXT:CONTEXT + cw]
            pred = (prob > THRESH).to("cpu", non_blocking=True).numpy().astype(np.uint8)
            pred_bin[y0:y1, x0:x1] = pred

        out[pred_bin == 1] = cls_id

    return out


# ======================================================= #
#                        Main                             #
# ======================================================= #

def resolve_output_path(output_arg: str, input_path: str, suffix: str = "_mask.png") -> str:
    """
    Resolve output path.

    If output_arg points to an existing directory, a file name is created from input_path.
    Otherwise, output_arg is treated as a file path.

    Args:
        output_arg (str): --output argument.
        input_path (str): Input image path.
        suffix (str): Suffix used when output_arg is a directory.

    Returns:
        str: Resolved output file path.
    """
    if os.path.isdir(output_arg):
        base = os.path.splitext(os.path.basename(input_path))[0]
        return os.path.join(output_arg, f"{base}{suffix}")

    # If it ends with a path separator, treat as directory even if it doesn't exist yet.
    if output_arg.endswith(os.sep) or output_arg.endswith("/"):
        os.makedirs(output_arg, exist_ok=True)
        base = os.path.splitext(os.path.basename(input_path))[0]
        return os.path.join(output_arg, f"{base}{suffix}")

    parent = os.path.dirname(output_arg)
    if parent:
        os.makedirs(parent, exist_ok=True)

    return output_arg


def main() -> None:
    """
    CLI entry point.

    Required:
        --input   : input image file path
        --output  : output file path OR output directory path

    Optional:
        --save_id     : also save raw ID mask as a single-channel PNG
        --save_overlay: save an overlay visualization (image + color mask)
    """
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", type=str, required=True, help="Input image path (BGR/RGB image readable by OpenCV).")
    parser.add_argument("--output", type=str, required=True, help="Output file path OR output directory.")
    parser.add_argument("--save_id", type=str, default=None, help="Optional output path for raw ID mask (uint8).")
    parser.add_argument("--save_overlay", type=str, default=None, help="Optional output path for overlay visualization.")
    args = parser.parse_args()

    input_path = args.input
    output_path = args.output

    if not os.path.exists(input_path):
        print(f"Error: input image not found: {input_path}")
        return

    # Select device.
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    # Load models (only existing checkpoints).
    models: Dict[str, torch.nn.Module] = {}
    for name, ckpt in CKPTS.items():
        if os.path.exists(ckpt):
            models[name] = load_bin_model(ckpt, device)

    if not models:
        print("Warning: no models were loaded. Check CKPTS paths.")
        return

    img = cv2.imread(input_path, cv2.IMREAD_COLOR)
    if img is None:
        print(f"Error: failed to read image: {input_path}")
        return

    print(f"Start inference for: {os.path.basename(input_path)}")
    pred_id = infer_one(models, img, device)

    # Save visualization (colorized mask).
    out_file = resolve_output_path(output_path, input_path, suffix="_mask.png")
    color = id_to_color(pred_id)
    if not cv2.imwrite(out_file, color):
        print(f"Error: failed to write output file: {out_file}")
        return

    # Optional: save raw ID mask.
    if args.save_id:
        save_id_path = resolve_output_path(args.save_id, input_path, suffix="_id.png")
        if not cv2.imwrite(save_id_path, pred_id):
            print(f"Warning: failed to write ID mask: {save_id_path}")

    # Optional: save overlay visualization.
    if args.save_overlay:
        save_ov_path = resolve_output_path(args.save_overlay, input_path, suffix="_overlay.png")
        ov = overlay(img, color, alpha=0.45)
        if not cv2.imwrite(save_ov_path, ov):
            print(f"Warning: failed to write overlay: {save_ov_path}")

    print(f"Inference complete. Mask saved to: {out_file}")


if __name__ == "__main__":
    main()
