# ===== import ===== #
import os

# Limit internal parallelism to keep CPU usage stable on Windows.
os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("MKL_NUM_THREADS", "1")

import random
import time
from pathlib import Path
from typing import List, Tuple

import cv2
import numpy as np
import torch
import torch.nn as nn
from torch.utils.data import Dataset, DataLoader
from tqdm import tqdm
import segmentation_models_pytorch as smp

# Limit OpenCV's internal threads (avoid "num_workers x cv threads" explosion).
cv2.setNumThreads(0)

# Use conservative CUDA backends for reproducibility/stability.
torch.backends.cuda.matmul.allow_tf32 = False
torch.backends.cudnn.allow_tf32 = False
torch.backends.cudnn.benchmark = False

# ======================================================= #
#                 Fixed paths / settings                  #
# ======================================================= #

DATA_DIR = Path("./data_5cls")     # Contains train.txt / val.txt (each line: "<img> <mask_id_png>")
CKPT_ROOT = Path("./ckpt_bin")     # Checkpoint root directory
CKPT_ROOT.mkdir(parents=True, exist_ok=True)

# Train target (one of: wall/window/door_other/door_swing/door_sliding)
TARGET = "door_sliding"

# Map class name -> class ID in the multi-class mask.
CLASS_TO_ID = {
    "wall": 1,
    "window": 2,
    "door_other": 3,
    "door_swing": 4,
    "door_sliding": 5,
}

# Hyperparameters (stability-oriented defaults).
EPOCHS = 40
GLOBAL_BS = 8
LR = 3e-4
WEIGHT_DECAY = 1e-4
PATIENCE = 6

PATCH_SIZE = 1024
CONTEXT = 128
TILES_PER_IMG = 1

# Windows: num_workers=0 is the safest. Increase only after validation.
NUM_WORKERS = 0
PIN_MEMORY = False

# ======================================================= #
#                       Utils                             #
# ======================================================= #

def seed_all(seed: int = 2025) -> None:
    """
    Seed Python, NumPy, and PyTorch RNGs for reproducibility.

    Args:
        seed (int): Seed value.
    """
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.cuda.manual_seed_all(seed)


# ------------------------------------------------------ #

def read_pairs(txt: Path) -> List[Tuple[str, str]]:
    """
    Read "<image_path> <mask_path>" pairs from a list file.

    Args:
        txt (Path): Path to train.txt or val.txt.

    Returns:
        list[tuple[str, str]]: List of (image_path, mask_path).
    """
    lines = txt.read_text(encoding="utf-8").splitlines()
    out: List[Tuple[str, str]] = []
    for ln in lines:
        sp = ln.strip().split()
        if len(sp) >= 2:
            out.append((sp[0], sp[1]))
    return out


# ------------------------------------------------------ #

def pad_to_min(img: np.ndarray, msk: np.ndarray, need_h: int, need_w: int) -> Tuple[np.ndarray, np.ndarray]:
    """
    Pad image/mask to ensure at least (need_h, need_w).

    Args:
        img (np.ndarray): BGR image buffer (H, W, 3).
        msk (np.ndarray): Mask buffer (H, W).
        need_h (int): Minimum height.
        need_w (int): Minimum width.

    Returns:
        tuple[np.ndarray, np.ndarray]: Padded (img, msk).
    """
    h, w = img.shape[:2]
    pad_h = max(0, need_h - h)
    pad_w = max(0, need_w - w)

    if pad_h or pad_w:
        img = cv2.copyMakeBorder(img, 0, pad_h, 0, pad_w, cv2.BORDER_CONSTANT, value=(0, 0, 0))
        msk = cv2.copyMakeBorder(msk, 0, pad_h, 0, pad_w, cv2.BORDER_CONSTANT, value=0)

    return img, msk


# ------------------------------------------------------ #

def random_crop(img: np.ndarray, msk: np.ndarray, h: int, w: int) -> Tuple[np.ndarray, np.ndarray]:
    """
    Randomly crop a region of size (h, w) from (img, msk).
    If the input is smaller than (h, w), it is expected to be padded before calling this.

    Args:
        img (np.ndarray): BGR image buffer (H, W, 3).
        msk (np.ndarray): Mask buffer (H, W).
        h (int): Crop height.
        w (int): Crop width.

    Returns:
        tuple[np.ndarray, np.ndarray]: Cropped (img, msk).
    """
    H, W = img.shape[:2]
    y0 = random.randint(0, max(0, H - h))
    x0 = random.randint(0, max(0, W - w))
    return img[y0:y0 + h, x0:x0 + w], msk[y0:y0 + h, x0:x0 + w]


# ------------------------------------------------------ #

def aug_basic(img: np.ndarray, msk: np.ndarray) -> Tuple[np.ndarray, np.ndarray]:
    """
    Apply simple geometric and photometric augmentations.

    Augmentations:
        - Horizontal flip (p=0.5)
        - Rotation by 0/90/180/270 degrees (uniform)
        - Brightness/contrast jitter (p=0.5)

    Args:
        img (np.ndarray): BGR image buffer (H, W, 3).
        msk (np.ndarray): Mask buffer (H, W).

    Returns:
        tuple[np.ndarray, np.ndarray]: Augmented (img, msk).
    """
    # 1) Horizontal flip
    if random.random() < 0.5:
        img = cv2.flip(img, 1)
        msk = cv2.flip(msk, 1)

    # 2) Rot90
    k = random.randint(0, 3)
    if k:
        img = np.ascontiguousarray(np.rot90(img, k))
        msk = np.ascontiguousarray(np.rot90(msk, k))

    # 3) Brightness/contrast
    if random.random() < 0.5:
        alpha = 1.0 + random.uniform(-0.1, 0.1)
        beta = random.uniform(-10, 10)
        img = cv2.convertScaleAbs(img, alpha=alpha, beta=beta)

    return img, msk


# ======================================================= #
#                       Dataset                           #
# ======================================================= #

class BinTileDataset(Dataset):
    """
    Dataset for binary segmentation from a multi-class ID mask.

    - The source mask is a grayscale PNG with IDs in {0..5}.
    - The dataset converts it into a binary mask for a given target ID.
    - Each sample is a context-aware tile:
        input size = (PATCH + 2*CONTEXT, PATCH + 2*CONTEXT)
      and the training loss is typically computed on the center PATCH region.

    Returns:
        x (torch.FloatTensor): Normalized image tensor [3, H, W] in [0,1].
        y (torch.FloatTensor): Binary mask tensor [1, H, W] in {0,1}.
    """

    def __init__(
        self,
        list_txt: Path,
        target_id: int,
        patch: int,
        ctx: int,
        tiles_per_img: int = 1,
        augment: bool = True,
    ):
        """
        Args:
            list_txt (Path): Path to train.txt listing "<img> <mask>".
            target_id (int): Target class ID to extract.
            patch (int): Core patch size (loss is computed on this region).
            ctx (int): Context margin added around the core patch.
            tiles_per_img (int): Number of random tiles sampled per image per epoch.
            augment (bool): Whether to apply augmentation.
        """
        self.pairs = read_pairs(list_txt)
        self.target_id = int(target_id)

        self.patch = int(patch)
        self.ctx = int(ctx)

        self.tiles_per_img = max(1, int(tiles_per_img))
        self.augment = bool(augment)

        self.in_h = self.patch + 2 * self.ctx
        self.in_w = self.patch + 2 * self.ctx

    def __len__(self) -> int:
        return len(self.pairs) * self.tiles_per_img

    def __getitem__(self, index: int) -> Tuple[torch.Tensor, torch.Tensor]:
        img_path, msk_path = self.pairs[index % len(self.pairs)]

        # 1) Read input image and ID mask.
        img = cv2.imread(img_path, cv2.IMREAD_COLOR)
        mc = cv2.imread(msk_path, cv2.IMREAD_GRAYSCALE)
        if img is None:
            raise FileNotFoundError(img_path)
        if mc is None:
            raise FileNotFoundError(msk_path)

        # 2) Ensure image/mask size match.
        if img.shape[:2] != mc.shape[:2]:
            mc = cv2.resize(mc, (img.shape[1], img.shape[0]), interpolation=cv2.INTER_NEAREST)

        # 3) Convert to binary mask for the target class.
        msk = (mc == self.target_id).astype(np.uint8)  # values in {0,1}

        # 4) Sample a context-aware tile.
        img, msk = pad_to_min(img, msk, self.in_h, self.in_w)
        img, msk = random_crop(img, msk, self.in_h, self.in_w)

        # 5) Optional augmentation.
        if self.augment:
            img, msk = aug_basic(img, msk)

        # 6) Convert to torch tensors.
        x = torch.from_numpy((img.astype(np.float32) / 255.0).transpose(2, 0, 1)).contiguous()
        y = torch.from_numpy(msk.astype(np.float32))[None, ...].contiguous()
        return x, y


# ======================================================= #
#                     Validation                           #
# ======================================================= #

def tiles_iter(H: int, W: int, core: int, overlap: int):
    """
    Generate tile coordinates for sliding-window style traversal.

    Args:
        H (int): Image height.
        W (int): Image width.
        core (int): Core tile size (without context).
        overlap (int): Overlap size in pixels.

    Yields:
        (y0, y1, x0, x1): Core window coordinates. Context is handled separately.
    """
    stride = max(1, core - overlap)

    ys = list(range(0, max(1, H - core + 1), stride))
    xs = list(range(0, max(1, W - core + 1), stride))

    # Ensure the last tile hits the boundary.
    if ys[-1] != H - core:
        ys.append(max(0, H - core))
    if xs[-1] != W - core:
        xs.append(max(0, W - core))

    for y0 in ys:
        for x0 in xs:
            y1 = min(y0 + core, H)
            x1 = min(x0 + core, W)
            yield y0, y1, x0, x1


# ------------------------------------------------------ #

@torch.no_grad()
def val_loss_fullimage(
    model: nn.Module,
    img_bgr: np.ndarray,
    mc_id: np.ndarray,
    target_id: int,
    core: int,
    overlap: int,
    ctx: int,
    device: torch.device,
    criterion,
) -> float:
    """
    Compute the validation loss for a full image by tiling.

    For each core tile:
        - Extract an input patch that includes context.
        - Run the model on the context-aware patch.
        - Crop the model output to the core region.
        - Compute criterion(logit_core, target_mask_core).

    Args:
        model (nn.Module): Segmentation model producing logits of shape [N,1,H,W].
        img_bgr (np.ndarray): Input BGR image buffer (H, W, 3).
        mc_id (np.ndarray): Multi-class ID mask (H, W) with values 0..5.
        target_id (int): Target class ID.
        core (int): Core tile size (PATCH_SIZE).
        overlap (int): Overlap between adjacent core tiles.
        ctx (int): Context margin around the core tile.
        device (torch.device): Target device.
        criterion (Callable): Loss function taking (logits, targets).

    Returns:
        float: Average loss over all pixels of the image.
    """
    H, W = img_bgr.shape[:2]
    total = 0.0
    pixels = 0

    for y0, y1, x0, x1 in tiles_iter(H, W, core, overlap):
        core_h = y1 - y0
        core_w = x1 - x0

        need_h = core_h + 2 * ctx
        need_w = core_w + 2 * ctx

        # 1) Extract a patch including context. Clamp to image bounds first.
        top, left = y0 - ctx, x0 - ctx
        bottom, right = y1 + ctx, x1 + ctx

        py0, px0 = max(0, top), max(0, left)
        py1, px1 = min(H, bottom), min(W, right)

        patch = img_bgr[py0:py1, px0:px1]

        # 2) Pad patch to exactly (need_h, need_w) if it crosses boundaries.
        pad_top = max(0, -top)
        pad_left = max(0, -left)
        pad_bottom = max(0, bottom - H)
        pad_right = max(0, right - W)

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

        # 3) Forward
        x = torch.from_numpy((patch.astype(np.float32) / 255.0).transpose(2, 0, 1)[None]).to(device)
        with torch.amp.autocast("cuda", enabled=(device.type == "cuda")):
            logit = model(x)  # [1,1,need_h,need_w]

        # 4) Crop logits to the core region (remove context).
        logit_core = logit[..., ctx:ctx + core_h, ctx:ctx + core_w].contiguous()

        # 5) Build target core mask.
        y_core_np = (mc_id[y0:y1, x0:x1] == target_id).astype(np.float32)
        y_core = torch.from_numpy(y_core_np)[None, None].to(device).contiguous()

        # 6) Accumulate pixel-weighted loss.
        loss = criterion(logit_core, y_core)
        total += loss.item() * (core_h * core_w)
        pixels += (core_h * core_w)

    return total / max(1, pixels)


# ======================================================= #
#                        Train                             #
# ======================================================= #

def train() -> None:
    """
    Main training entry point.
    - Trains a single TARGET as a binary segmentation problem.
    - Uses tiles with context for training and full-image tiling for validation.
    """
    # Validate target configuration and prepare checkpoint directory.
    if TARGET not in CLASS_TO_ID:
        raise ValueError(f"TARGET '{TARGET}' not in {list(CLASS_TO_ID)}")

    target_id = CLASS_TO_ID[TARGET]
    ckpt_dir = CKPT_ROOT / TARGET
    ckpt_dir.mkdir(parents=True, exist_ok=True)

    # Seed all RNGs for reproducible sampling and initialization.
    seed_all(2025)

    # Select compute device (GPU if available).
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    # Load train/val pairs.
    tr_list = DATA_DIR / "train.txt"
    va_list = DATA_DIR / "val.txt"
    if not (tr_list.exists() and va_list.exists()):
        raise FileNotFoundError("train.txt/val.txt is missing under DATA_DIR")

    tr_ds = BinTileDataset(tr_list, target_id, PATCH_SIZE, CONTEXT, TILES_PER_IMG, augment=True)
    va_pairs = read_pairs(va_list)

    tr_dl = DataLoader(
        tr_ds,
        batch_size=GLOBAL_BS,
        shuffle=True,
        num_workers=NUM_WORKERS,
        pin_memory=PIN_MEMORY,
    )

    # Create model (binary output: classes=1, activation=None => logits).
    model = smp.DeepLabV3Plus(
        encoder_name="resnet34",
        encoder_weights="imagenet",
        in_channels=3,
        classes=1,
        activation=None,
    ).to(device)

    # Losses
    bce = nn.BCEWithLogitsLoss()
    dice = smp.losses.DiceLoss(mode="binary", from_logits=True)

    def criterion(logit: torch.Tensor, y: torch.Tensor) -> torch.Tensor:
        """
        Combined BCE + Dice loss.

        Args:
            logit (torch.Tensor): Logits tensor [N,1,H,W].
            y (torch.Tensor): Target tensor [N,1,H,W] in {0,1}.

        Returns:
            torch.Tensor: Scalar loss.
        """
        logit = logit.contiguous()
        y = y.contiguous()
        return 0.5 * bce(logit, y) + 0.5 * dice(logit, y)

    # Optimizer / AMP
    opt = torch.optim.AdamW(model.parameters(), lr=LR, weight_decay=WEIGHT_DECAY)
    scaler = torch.amp.GradScaler("cuda", enabled=(device.type == "cuda"))

    # ---------------------- #
    #        Resume          #
    # ---------------------- #
    last_p = ckpt_dir / "last.pth"
    best_p = ckpt_dir / "best.pth"

    start_ep = 1
    best_val = float("inf")
    bad = 0

    if last_p.exists():
        sd = torch.load(str(last_p), map_location="cpu")
        model.load_state_dict(sd["model"])
        opt.load_state_dict(sd["opt"])
        if sd.get("scaler") is not None:
            scaler.load_state_dict(sd["scaler"])
        start_ep = int(sd.get("epoch", 0)) + 1
        best_val = float(sd.get("best", float("inf")))
        bad = int(sd.get("bad", 0))
        print(f"[Resume {TARGET}] epoch={start_ep} best={best_val:.4f} bad={bad}")

    # ---------------------- #
    #         Info           #
    # ---------------------- #
    print(f"Device={device} | TARGET={TARGET}({target_id}) | BS={GLOBAL_BS} | PATCH={PATCH_SIZE} | CTX={CONTEXT}")
    if device.type == "cuda":
        print("GPU:", torch.cuda.get_device_name(0), "| Torch:", torch.__version__)
    print(f"Train pairs={len(tr_ds)//TILES_PER_IMG:,} | Val pairs={len(va_pairs):,}")

    # ---------------------- #
    #        Training        #
    # ---------------------- #
    for ep in range(start_ep, EPOCHS + 1):
        model.train()
        t0 = time.time()

        run_sum = 0.0
        run_n = 0

        pbar = tqdm(tr_dl, desc=f"[{TARGET}][Epoch {ep}/{EPOCHS}]")
        opt.zero_grad(set_to_none=True)

        for x, y in pbar:
            # x: [N,3,H_in,W_in], y: [N,1,H_in,W_in]
            x = x.to(device)
            y = y.to(device)

            with torch.amp.autocast("cuda", enabled=(device.type == "cuda")):
                logit = model(x)  # [N,1,H_in,W_in]

                # Compute loss only on the center PATCH region.
                logit_c = logit[..., CONTEXT:CONTEXT + PATCH_SIZE, CONTEXT:CONTEXT + PATCH_SIZE].contiguous()
                y_c = y[..., CONTEXT:CONTEXT + PATCH_SIZE, CONTEXT:CONTEXT + PATCH_SIZE].contiguous()
                loss = criterion(logit_c, y_c)

            scaler.scale(loss).backward()
            scaler.step(opt)
            scaler.update()
            opt.zero_grad(set_to_none=True)

            run_sum += loss.item() * x.size(0)
            run_n += x.size(0)

            pbar.set_postfix(
                loss=f"{loss.item():.4f}",
                avg=f"{run_sum / max(1, run_n):.4f}",
                lr=f"{opt.param_groups[0]['lr']:.2e}",
            )

        tr_loss = run_sum / max(1, run_n)

        # ---------------------- #
        #       Validation       #
        # ---------------------- #
        model.eval()

        va_sum = 0.0
        va_px = 0

        with torch.no_grad():
            for ip, mp in va_pairs:
                img = cv2.imread(ip, cv2.IMREAD_COLOR)
                mc = cv2.imread(mp, cv2.IMREAD_GRAYSCALE)
                if img is None or mc is None:
                    continue

                if img.shape[:2] != mc.shape[:2]:
                    mc = cv2.resize(mc, (img.shape[1], img.shape[0]), interpolation=cv2.INTER_NEAREST)

                vloss = val_loss_fullimage(
                    model=model,
                    img_bgr=img,
                    mc_id=mc,
                    target_id=target_id,
                    core=PATCH_SIZE,
                    overlap=PATCH_SIZE // 4,
                    ctx=CONTEXT,
                    device=device,
                    criterion=criterion,
                )

                va_sum += vloss * (img.shape[0] * img.shape[1])
                va_px += (img.shape[0] * img.shape[1])

        va_loss = va_sum / max(1, va_px)

        dt = time.time() - t0
        print(f"Epoch {ep:02d} | train={tr_loss:.4f} | val={va_loss:.4f} | {dt:.1f}s")

        # ---------------------- #
        #      Checkpoints       #
        # ---------------------- #
        torch.save(
            {"epoch": ep, "model": model.state_dict(), "opt": opt.state_dict(),
             "scaler": scaler.state_dict(), "best": best_val, "bad": bad},
            str(last_p),
        )

        if va_loss < best_val - 1e-4:
            best_val = va_loss
            bad = 0
            torch.save(
                {"epoch": ep, "model": model.state_dict(), "opt": opt.state_dict(),
                 "scaler": scaler.state_dict(), "best": best_val, "bad": bad},
                str(best_p),
            )
            print("  Best updated:", best_p.as_posix())
        else:
            bad += 1
            print(f"  No improve ({bad}/{PATIENCE})")
            if bad >= PATIENCE:
                print("Early stop.")
                break

    print(f"[{TARGET}] Done. Best val={best_val:.4f}")


# ======================================================= #

if __name__ == "__main__":
    train()
