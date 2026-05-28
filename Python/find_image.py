"""Image matching using OpenCV template matching."""
import sys
import json
import numpy as np
import cv2
from PIL import ImageGrab


def find_template(screen: np.ndarray, template: np.ndarray, threshold: float) -> dict:
    """Run OpenCV template matching and return result."""
    result = cv2.matchTemplate(screen, template, cv2.TM_CCOEFF_NORMED)
    min_val, max_val, min_loc, max_loc = cv2.minMaxLoc(result)

    if max_val >= threshold:
        h, w = template.shape[:2]
        center_x = max_loc[0] + w // 2
        center_y = max_loc[1] + h // 2
        return {"found": True, "centerX": center_x, "centerY": center_y, "score": float(max_val)}
    return {"found": False, "centerX": 0, "centerY": 0, "score": float(max_val)}


def main():
    if len(sys.argv) < 3:
        print(json.dumps({"found": False, "error": "Usage: find_image.py <template_path> <threshold_0_100>"}))
        sys.exit(1)

    template_path = sys.argv[1]
    threshold_pct = float(sys.argv[2])
    threshold = max(0.0, min(1.0, threshold_pct / 100.0))

    try:
        template = cv2.imread(template_path, cv2.IMREAD_COLOR)
        if template is None:
            print(json.dumps({"found": False, "error": f"Cannot read template: {template_path}"}))
            sys.exit(1)

        screen_pil = ImageGrab.grab(all_screens=True)
        screen = cv2.cvtColor(np.array(screen_pil), cv2.COLOR_RGB2BGR)

        result = find_template(screen, template, threshold)
        print(json.dumps(result))
    except Exception as e:
        print(json.dumps({"found": False, "error": str(e)}))
        sys.exit(1)


if __name__ == "__main__":
    main()
