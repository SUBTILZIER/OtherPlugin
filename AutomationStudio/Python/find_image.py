"""Image matching using OpenCV template matching."""
import sys
import json
import numpy as np
import cv2
from PIL import ImageGrab


def find_template(screen: np.ndarray, template: np.ndarray, threshold: float, offset_x: int = 0, offset_y: int = 0) -> dict:
    """Run OpenCV template matching and return result."""
    result = cv2.matchTemplate(screen, template, cv2.TM_CCOEFF_NORMED)
    min_val, max_val, min_loc, max_loc = cv2.minMaxLoc(result)

    if max_val >= threshold:
        h, w = template.shape[:2]
        x = max_loc[0] + offset_x
        y = max_loc[1] + offset_y
        center_x = x + w // 2
        center_y = y + h // 2
        return {
            "found": True,
            "centerX": center_x,
            "centerY": center_y,
            "x": x,
            "y": y,
            "width": w,
            "height": h,
            "score": float(max_val),
        }
    return {"found": False, "centerX": 0, "centerY": 0, "score": float(max_val)}


def main():
    if len(sys.argv) < 3:
        if len(sys.argv) == 2 and sys.argv[1].lower().endswith(".json"):
            with open(sys.argv[1], "r", encoding="utf-8") as f:
                request = json.load(f)
            template_path = request.get("template_path", "")
            source_mode = request.get("source_mode", "RealtimeScreenshot")
            source_image_path = request.get("source_image_path", "")
            threshold_pct = float(request.get("threshold_percent", 80))
            use_region = bool(request.get("use_region", False))
            region_x = int(float(request.get("region_x", 0)))
            region_y = int(float(request.get("region_y", 0)))
            region_width = int(float(request.get("region_width", 0)))
            region_height = int(float(request.get("region_height", 0)))
        else:
            print(json.dumps({"found": False, "error": "Usage: find_image.py <request.json> or <template_path> <threshold_0_100>"}))
            sys.exit(1)
    else:
        template_path = sys.argv[1]
        source_mode = "RealtimeScreenshot"
        source_image_path = ""
        threshold_pct = float(sys.argv[2])
        use_region = False
        region_x = 0
        region_y = 0
        region_width = 0
        region_height = 0

    threshold = max(0.0, min(1.0, threshold_pct / 100.0))

    try:
        template = cv2.imread(template_path, cv2.IMREAD_COLOR)
        if template is None:
            print(json.dumps({"found": False, "error": f"Cannot read template: {template_path}"}))
            sys.exit(1)

        if str(source_mode).lower() == "manualimage":
            screen = cv2.imread(source_image_path, cv2.IMREAD_COLOR)
            if screen is None:
                print(json.dumps({"found": False, "error": f"Cannot read source image: {source_image_path}"}))
                sys.exit(1)
            screen_pil = None
        else:
            screen_pil = ImageGrab.grab(all_screens=True)
            screen = None

        offset_x = 0
        offset_y = 0
        if use_region:
            if region_width <= 0 or region_height <= 0:
                print(json.dumps({"found": False, "error": "Invalid region size"}))
                sys.exit(1)

            if screen is not None:
                screen = screen[region_y:region_y + region_height, region_x:region_x + region_width]
            else:
                screen_pil = screen_pil.crop((
                    region_x,
                    region_y,
                    region_x + region_width,
                    region_y + region_height,
                ))
            offset_x = region_x
            offset_y = region_y

        if screen is None:
            screen = cv2.cvtColor(np.array(screen_pil), cv2.COLOR_RGB2BGR)

        result = find_template(screen, template, threshold, offset_x, offset_y)
        print(json.dumps(result))
    except Exception as e:
        print(json.dumps({"found": False, "error": str(e)}))
        sys.exit(1)


if __name__ == "__main__":
    main()
