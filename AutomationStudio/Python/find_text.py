"""Screen text recognition using EasyOCR."""
import sys
import json
import os


def main():
    if len(sys.argv) < 3:
        if len(sys.argv) == 2 and sys.argv[1].lower().endswith(".json"):
            with open(sys.argv[1], "r", encoding="utf-8") as f:
                request = json.load(f)
            search_text = request.get("search_text", "")
            threshold_pct = float(request.get("threshold_percent", 80))
        else:
            result = {"found": False, "error": "Usage: find_text.py <request.json> or <search_text> <threshold_0_100>"}
            print(json.dumps(result), flush=True)
            sys.exit(0)
    else:
        search_text = sys.argv[1]
        threshold_pct = float(sys.argv[2])

    threshold = max(0.0, min(1.0, threshold_pct / 100.0))

    # Debug header to stderr so it appears in log
    print(f"[find_text] search='{search_text}' threshold={threshold_pct}% ({threshold:.2f})", file=sys.stderr, flush=True)

    try:
        import easyocr
    except ImportError:
        result = {
            "found": False,
            "error": "EasyOCR not installed. Run: pip install easyocr torch torchvision",
        }
        print(json.dumps(result), flush=True)
        print("[find_text] EasyOCR import failed - not installed", file=sys.stderr, flush=True)
        sys.exit(0)

    try:
        import numpy as np
        from PIL import ImageGrab

        # Check for cached reader to avoid repeated model loading
        reader = None
        cache_key = "easyocr_reader_chs_en"
        if cache_key not in globals():
            print(f"[find_text] Loading EasyOCR model...", file=sys.stderr, flush=True)
            reader = easyocr.Reader(['ch_sim', 'en'], gpu=False, verbose=False)

        print(f"[find_text] Capturing screen...", file=sys.stderr, flush=True)
        screen = ImageGrab.grab(all_screens=True)
        print(f"[find_text] Screen size: {screen.size}", file=sys.stderr, flush=True)

        screen_np = np.array(screen)
        print(f"[find_text] Running OCR...", file=sys.stderr, flush=True)
        results = reader.readtext(screen_np)
        print(f"[find_text] Detected {len(results)} text blocks", file=sys.stderr, flush=True)

        # Print top matches for debugging
        for i, (bbox, text, confidence) in enumerate(results):
            if i < 10:  # Print first 10 for debug
                print(f"[find_text]   [{i}] conf={confidence:.2f} text='{text}'", file=sys.stderr, flush=True)

        for (bbox, text, confidence) in results:
            if search_text.lower() in text.lower() and confidence >= threshold:
                cx = int((bbox[0][0] + bbox[2][0]) / 2)
                cy = int((bbox[0][1] + bbox[2][1]) / 2)
                result = {
                    "found": True,
                    "centerX": cx,
                    "centerY": cy,
                    "score": float(confidence),
                    "matchedText": text,
                }
                print(json.dumps(result), flush=True)
                print(f"[find_text] MATCH: '{text}' at ({cx},{cy}) score={confidence:.2f}", file=sys.stderr, flush=True)
                return

        print(f"[find_text] No match found for '{search_text}' in {len(results)} blocks", file=sys.stderr, flush=True)
        print(json.dumps({"found": False, "centerX": 0, "centerY": 0, "score": 0}), flush=True)

    except Exception as e:
        result = {"found": False, "error": str(e)}
        print(json.dumps(result), flush=True)
        print(f"[find_text] Error: {e}", file=sys.stderr, flush=True)


if __name__ == "__main__":
    main()
