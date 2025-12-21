# ===== import ===== #
import os

os.environ.setdefault("OMP_NUM_THREADS", "1")
os.environ.setdefault("OPENBLAS_NUM_THREADS", "1")
os.environ.setdefault("MKL_NUM_THREADS", "1")

import json, random, traceback
import cv2
import numpy as np
from pycocotools.coco import COCO
from pycocotools import mask as mutils
from concurrent.futrues import ThreadPoolExecutor, as as_completed

# Limit OpenCV's internal thread. "pool size(WORKER_CAP) x cv threads" can explode otherwise.
cv2.setNumThreads(0) 

# ===== Fixed paths / settings ===== #
IMAGES_DIR      = r"../FloorplanSegmentation/data/images"   # Input images directory
ANN_DIR         = r"../FloorplanSegmentation/data/labels"   # COCO annotations directory
OUT_DIR         = r"./output_predata"                       # Output root directory
WORKER_CAP      = 32                                        # Upper bound on worker threads
TEMP_SUFFIX     = ".tmp"                                    # Temp file suffix
PROGRESS_LOG    = "progress.jsonl"                          # Progress log file path
ERROR_LOG       = "errors.log"                              # Error log file path

# =========================== #
#            Utils            #
# =========================== #

def rebuild_list(img_list, out_dir, list_path):
    """
    Rebuild a list file from existing outputs.
    - Writes a line "<image_path> <mask_path>" to 'list_path'.

    Args:
        img_list (Sequence[str])    : List of input image paths.
        out_dir (str)               : Directory where whe corresponding masks are stored.
        list_path (str)             : Destination path of the list file to generate.
    """
    with open(list_path, "w", encoding="utf-8") as file:
        for path in img_list:
            base = os.path.splitext(os.path.basename(p))[0]
            out_mask = os.path.join(out_dir, f"{base}_mc5.png")
            if os.path.exists(out_mask):
                file.write(path + " " + out_mask + "\n")

# ======================================================= #

def safe_write_png(out_path: str, img: np.ndarray):
    """
    Save a class-mask buffer as a PNG image file.

    Args:
        out_path(str)   : Destination file path for the output PNG.
        img(np.darray)  : Class label mask buffer (assumed uint8, shape HxW)

    Raises:
        RuntimeError: If PNG encoding fails.
    """

    base, ext = os.path.splitext(out_path)   # Split destination into base name and extension (base: .._mc5, ext: .png).
    tmp = f"{base}{TEMP_SUFFIX}{ext}"        # Temp file path (.._mc5.tmp.png)

    # Encode the mask as PNG bytes.
    ok, buf = cv2.imencode(".png", img)
    if not ok:
        raise RuntimeError("OpenCV imencode('.png') failed")

    # Write to a temporary file first.
    with open(tmp, "wb") as file:
        file.write(buf.tobytes())
    
    # Atomically replace the target path to avoid partial files.
    os.replace(tmp, out_path)

# ======================================================= #

def load_coco(json_path):
    """
    Load a COCO-format JSON file and build indices.

    Args :
        json_path (str)     : Path to the COCO annotation JSON file.
    
    Returns:
        COCO : An initialized pycocotools COCO object with indices created.
    
    Raises:
        RuntimeError: If the file is missing, the content is not valid UTF-8, or the JSON structure is invalid.
    """
    try:
        with open(json_path, "r", encoding="utf-8-sig") as file:
            dataset = json.load(file)

        coco = COCO()
        coco.dataset = dataset
        coco.createIndex()
        return coco
    
    except (FileNotFoundError, UnicodeError, json.JSONDecodeError) as err:
        raise RuntimeError(f"Failed to load COCO JSON: {json_path} - {err}") from err

# ======================================================= #

def annotation_to_mask(coco, ann, h, w):
    """
    Convert a COCO annotation's segmentation into a binary mask.
    - Polygon (list)     : Each path is rasterized and filled into the mask. 
    - RLE (dictionary)   : Decoded directly and scaled from {0,1} to {0,255}.

    Args:
        coco (COCO)     : Initialized pycocotools COCO object (indices created).
        ann (dict)      : A single COCO annotation record.
        h (int)         : Target mask height (must match the image height).
        w (int)         : Target mask width (must match the image width).
    
    Returns:
        np.ndarray      : Binary mask of shape(h, w), dtype uint8 with values {0, 255}
    """
    seg = ann.get("segmentation")

    # Polygon case
    if isinstance(seg, list):
        m = np.zeros((h, w), np.uint8)
        for s in seg:
            pts = np.array(s, dtype=np.float32).reshape(-1, 2).astype(np.int32)
            cv2.fillPoly(m, [pts], 255)
        return m

    # RLE case
    elif isinstance(seg, dict):
        return (mutils.decode(seg).astype(np.uint8) * 255)

    # Fallback : neither polygon nor RLE
    return np.zeros((h, w), np.uint8)

# ======================================================= #

def classify(cat_name, attrs: dict):
    """
    Map a raw COCO category (name + attributes) to the normalized class ID.
    - (background=0, wall=1, window=2, other door=3, sliding door=4, hinged door=5)

    Args:
        cat_name(str)   : Raw category name from COCO (e.g., "벽체", "창호", "출입문").
        attrs(dict)     : Annotation attributes.
    """
    name = cat_name or ""

    # 1) Wall
    if "벽체" in name:
        return 1

    # 2) Window
    if "창호" in name:
        return 2

    # 3) Door - Hinge, Slide, Others
    if "출입문" in name:
        attr_val = attrs.get("구조_출입문", None)
        if not attr_val:
            return 3
        v = str(attr_val)
        if "여닫" in v: return 5
        if "미닫" in v: return 4
        return 3

    # 4) Background
    return 0

# ======================================================= #

def collect_images(images_dir):
    """
    Collect image file paths for training.

    Args:
        images_dir  : Path to the directory that contains raw images.
    
    Returns:
        list[str]   : List of image file paths.
    """
    # Return error if the directory does not exist.
    if not os.path.isdir(images_dir):
        raise FileNotFoundError(f"Directory not found: {images_dir}")
    
    # Allowed image extensions.
    exts = (".png", ".jpg", ".jpeg")
    kept = []

    # Scan directory and keep files whose names have allowed extensions.
    with os.scandir(images_dir) as it:
        for entry in it:
            if entry.is_file() and entry.name.lower().endswith(exts):
                kept.append(entry.path)

    return sorted(kept)

# ======================================================= #

def split_images(images, seed) :
    """
    Randomly split a list of images into train/val sets.

    Args:
        images      : List of image paths.
        seed        : Seed for reproducible randomness.

    Returns:
        tuple[list[str], list[str]] : Two lists split by the ratio. (train, val)
    """
    val_ratio = 0.1
    n = len(images)
    
    if n == 0:
        return [], []

    randGen = random.Random(seed)

    # Decide exact counts.
    train_count = int(round(n * (1.0 - val_ratio)))

    # Shuffle randomly and split the images into train/val sets.
    indices = list(range(n))
    randGen.shuffle(indices)
    train_idx = set(indices[:train_count])

    train = [images[i] for i in range(n) if i in train_idx]
    val   = [images[i] for i in range(n) if i not in train_idx]

    return train, val

# ======================================================= #

def add_jobs(img_list, out_dir, jobs):
    """
    Enqueue one processing job per image.

    Args:
        img_list    : List of input image paths to process.
        out_dir     : Directory where output masks will be saved.
        jobs        : List to append job dictionaries to (acts as the job queue).
    """
    for img_path in img_list:
        base = os.path.splitext(os.path.basename(img_path))[0]  # filename without extension
        out_mask = os.path.join(out_dir, f"{base}_mc5.png")     # output mask filename
        
        jobs.append({
            "img_path":img_path,    # raw image path
            "ann_dir":ANN_DIR,      # annotation directory
            "out_mask":out_mask})   # destination mask path

# ======================================================= #

# =========================== #
#            Worker           #
# =========================== #
def process_one(job):
    """
    Build and save a 6-level label mask for a single image.

    Pipeline:
        1) Load the paired COCO JSON
        2) Allocate an (H, W) uint8 label map.
        3) Overlay class mask in the exact order.
            (0:background, 1:wall, 2:window, 3:other door, 4:sliding door, 5:hinged door)
        4) Save as PNG

    Args:
        job (dict)  = {
          'img_path':...,   # raw image path
          'ann_dir':...,    # directory containing annotation JSON
          'out_mask':...    # destination path for the output mask PNG
        }
    
    Returns :
        dict: {
            "status"    : "ok" | "warn" | "err",
            "img"       : str,
            "out"       : str,
            "msg"       : str
        }
    """
    img_path  = job["img_path"]
    ann_dir   = job["ann_dir"]
    out_mask  = job["out_mask"]

    # Derive the paired JSON path from the raw image basename.
    base = os.path.splitext(os.path.basename(img_path))[0]
    json_path = os.path.join(ann_dir, base + ".json")

    # If no matching JSON file is found, returns an error.
    if not os.path.exists(json_path):
        return {"status":"warn", "img":img_path, "out":out_mask, "msg":"JSON not found"}

    try:
        # 1) Load COCO and build indices.
        coco = load_coco(json_path)

        # 2) Read image record and allocate label map.
        imginfo = coco.loadImgs(coco.getImgIds())[0]
        h, w = imginfo["height"], imginfo["width"] 

        # 3) Collect annotations of this image.
        ann_ids = coco.getAnnIds(imgIds=[imginfo["id"]], iscrowd=None)
        anns = coco.loadAnns(ann_ids)

        # Build category_id
        category_map = {c["id"]: str(c.get("name", "")) for c in coco.loadCats(coco.getCatIds())}

        buckets = {1: [], 2: [], 3: [], 4: [], 5: []}  # 0(background) is implicit
        for ann in anns:
            cat_name = category_map.get(ann.get("category_id"), "")
            attrs    = ann.get("attributes", {})
            cls      = classify(cat_name, attrs)  # 0..5 (our final classes)
            if cls in buckets:                    # keep only {1..5}
                buckets[cls].append(ann)

        # 4) Overlay passes
        # 4-1) Wall
        for ann in buckets[1]:
            m = annotation_to_mask(coco, ann, h, w)
            class_mask = np.maximum(class_mask, (m > 0).astype(np.uint8))

        # 4-2) Window
        for ann in buckets[2]:
            m = annotation_to_mask(coco, ann, h, w)
            class_mask[m>0] = 2

        # 4-3) Door
        for ann in (3, 4, 5):
            for ann in buckets[cid]:
                m = annotation_to_mask(coco, ann, h, w)
                class_mask[m>0] = cid

        # 5) Basic validation : warn if everything is background.
        if np.count_nonzero(class_mask) == 0:
            return {"status":"warn", "img":img_path, "out":out_mask, "msg":"all zeros"}

        # 6) Save
        safe_write_png(out_mask, class_mask)

        # 7) Success
        return {"status":"ok", "img":img_path, "out":out_mask}

    # Error during processing
    except Exception as e:
        return {"status":"err", "img":img_path, "out":out_mask,
                "msg": f"{e}\n{traceback.format_exc()}"}
    
# ======================================================= #

def main():
    # Configure the output folders.
        # ./OUT_DIR
        # ./OUT_DIR/train
        # ./OUT_DIR/val
    os.makedirs(OUT_DIR, exist_ok=True)
    out_train = os.path.join(OUT_DIR, "train")
    out_val   = os.path.join(OUT_DIR, "val")
    os.makedirs(out_train, exist_ok=True)
    os.makedirs(out_val, exist_ok=True)

    # Collect raw images (for training).
    imgs = collect_images(IMAGES_DIR)
    if not imgs :
        print(f"[WARNING] Can't find image files: {IMAGES_DIR}")

    # Train/Val split.
    train_list, val_list = split_images(imgs, 42)

    # Configure the multithreading job queue (one job per image).
    jobs = []
    add_jobs(train_list, out_train, jobs)
    add_jobs(val_list, out_val, jobs)

    # Compute the number of workers.
    cores = os.cpu_count() or 4                     # Number of CPU cores(fallback to 4 if None).
    workers = max(1, min(WORKER_CAP, cores * 5))    # decide worker count with upper/lower bounds.

    # Run parallel jobs.
    results = []
    if jobs:
        # Create a thread pool and open log files.
        with ThreadPoolExecutor(max_workers=workers) as pool, \
             open(os.path.join(OUT_DIR, PROGRESS_LOG), "w", encoding="utf-8") as plog, \
             open(os.path.join(OUT_DIR, ERROR_LOG), "w", encoding="utf-8") as elog:
            
            # Submit tasks (threads are created internally by the pool as needed).
            futs = [pool.submit(process_one, j) for j in jobs]

            # Process completed tasks.
            for fut in as_completed(futs):
                r = fut.result()
                results.append(r)

                # Stream logs to files based on status.
                if r["status"] in ("ok", "skip", "warn"):
                    plog.write(json.dumps(r, ensure_ascii=False) + "\n"); plog.flush()
                else:
                    elog.write(json.dumps(r, ensure_ascii=False) + "\n"); elog.flush()
    else:
        print("[Note] There are no new jobs to be processed.")
    
    rebuild_list(train_list, out_train, os.path.join(OUT_DIR, "train.txt"))
    rebuild_list(val_list, out_val, os.path.join(OUT_DIR, "val.txt"))

    # Summary
    cnt = {"ok":0, "skip":0, "warn":0, "err":0}
    for r in results:
        cnt[r["status"]] = cnt.get(r["status"], 0) + 1
    print("Done:", OUT_DIR, "| workers:", workers)
    print("Summary:", cnt)

# ======================================================= #

if __name__ == "__main__" :
    main()