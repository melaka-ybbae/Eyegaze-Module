"""
ONNX Runtime + 9-point Calibration demo

Usage:
    python demo/onnx_calibrated_demo.py --model weights/L2CSNet_gaze360.onnx

Controls:
    'c' - Run calibration
    's' - Save calibration
    'l' - Load calibration
    'r' - Reset calibration
    'q' - Quit
"""

import argparse
import pathlib
import sys
import time
import os

# Add cuDNN to PATH if installed via pip
try:
    import nvidia.cudnn
    cudnn_path = pathlib.Path(nvidia.cudnn.__path__[0]) / 'bin'
    if cudnn_path.exists():
        os.environ['PATH'] = str(cudnn_path) + os.pathsep + os.environ.get('PATH', '')
except ImportError:
    pass

import cv2
import numpy as np
import onnxruntime as ort

# Add project root
PROJECT_ROOT = pathlib.Path(__file__).parent.parent
sys.path.insert(0, str(PROJECT_ROOT))

from calibration import GazeCalibrator, PolynomialMapper, GazeFilter, create_gaze_filter

# Try to import face detection
try:
    from face_detection import RetinaFace
    HAS_FACE_DETECTION = True
except ImportError:
    HAS_FACE_DETECTION = False
    print("Warning: face_detection not installed. Using OpenCV cascade.")


class ONNXCalibratedGazeTracker:
    """ONNX-based gaze tracker with screen calibration."""

    def __init__(
        self,
        model_path: str,
        screen_width: int = 1920,
        screen_height: int = 1080,
        use_gpu: bool = True
    ):
        self.screen_width = screen_width
        self.screen_height = screen_height

        # Set up ONNX Runtime
        if use_gpu:
            providers = ['CUDAExecutionProvider', 'CPUExecutionProvider']
        else:
            providers = ['CPUExecutionProvider']

        self.session = ort.InferenceSession(model_path, providers=providers)
        print(f"ONNX Runtime providers: {self.session.get_providers()}")

        self.input_name = self.session.get_inputs()[0].name
        self.output_name = self.session.get_outputs()[0].name

        # Face detector
        if HAS_FACE_DETECTION:
            self.face_detector = RetinaFace()
        else:
            cascade_path = cv2.data.haarcascades + 'haarcascade_frontalface_default.xml'
            self.face_detector = cv2.CascadeClassifier(cascade_path)

        # Preprocessing params
        self.mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
        self.std = np.array([0.229, 0.224, 0.225], dtype=np.float32)

        # Calibration
        self.mapper = None
        self.is_calibrated = False

        # Smoothing filter
        self.gaze_filter = None
        self.screen_filter = None
        self.filter_enabled = True

        # Current state
        self.current_pitch = None
        self.current_yaw = None
        self.current_bbox = None

    def enable_filter(self, preset: str = "balanced"):
        """Enable smoothing filter with preset."""
        self.gaze_filter = create_gaze_filter(preset)
        self.screen_filter = create_gaze_filter(preset)
        self.filter_enabled = True
        print(f"Filter enabled: {preset}")

    def disable_filter(self):
        """Disable smoothing filter."""
        self.filter_enabled = False
        if self.gaze_filter:
            self.gaze_filter.reset()
        if self.screen_filter:
            self.screen_filter.reset()
        print("Filter disabled")

    def preprocess(self, face_img: np.ndarray) -> np.ndarray:
        # Match L2CS Pipeline: resize to 448 first, then center crop or resize to 224
        # The L2CS transforms.Resize(448) resizes the shorter edge to 448
        face_img = cv2.resize(face_img, (448, 448))
        face_img = cv2.cvtColor(face_img, cv2.COLOR_BGR2RGB)

        # Convert to PIL-like format for proper normalization
        face_img = face_img.astype(np.float32) / 255.0
        face_img = (face_img - self.mean) / self.std
        face_img = face_img.transpose(2, 0, 1)
        return np.expand_dims(face_img, 0).astype(np.float32)

    def detect_faces(self, frame: np.ndarray):
        if HAS_FACE_DETECTION:
            faces = self.face_detector(frame)
            if faces is None:
                return []
            return [(box, score) for box, landmark, score in faces if score > 0.5]
        else:
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            detections = self.face_detector.detectMultiScale(gray, 1.1, 5, minSize=(60, 60))
            return [([x, y, x+w, y+h], 1.0) for (x, y, w, h) in detections]

    def process_frame(self, frame: np.ndarray):
        """Process frame and return gaze + screen point."""
        screen_point = None
        self.current_pitch = None
        self.current_yaw = None
        self.current_bbox = None

        faces = self.detect_faces(frame)
        if not faces:
            return None, None

        # Use first face
        box, score = faces[0]
        x_min, y_min, x_max, y_max = [int(v) for v in box[:4]]
        x_min = max(0, x_min)
        y_min = max(0, y_min)
        x_max = min(frame.shape[1], x_max)
        y_max = min(frame.shape[0], y_max)

        if x_max <= x_min or y_max <= y_min:
            return None, None

        # Extract and run inference
        face_img = frame[y_min:y_max, x_min:x_max]
        input_tensor = self.preprocess(face_img)
        output = self.session.run([self.output_name], {self.input_name: input_tensor})
        gaze = output[0][0]

        self.current_pitch = float(gaze[0])
        self.current_yaw = float(gaze[1])
        self.current_bbox = [x_min, y_min, x_max, y_max]

        # Apply smoothing filter to gaze angles
        if self.filter_enabled and self.gaze_filter is not None:
            self.current_pitch, self.current_yaw = self.gaze_filter.filter(
                self.current_pitch, self.current_yaw
            )

        # Map to screen if calibrated
        if self.is_calibrated and self.mapper is not None:
            gaze_input = np.array([self.current_pitch, self.current_yaw])
            screen_point = self.mapper.predict(gaze_input)

            # Apply smoothing filter to screen coordinates
            if self.filter_enabled and self.screen_filter is not None:
                screen_point = np.array(self.screen_filter.filter(
                    screen_point[0], screen_point[1]
                ))

            screen_point = np.clip(screen_point, [0, 0],
                                   [self.screen_width - 1, self.screen_height - 1])

        return (self.current_pitch, self.current_yaw), screen_point

    def get_current_gaze(self):
        if self.current_pitch is not None:
            return (self.current_pitch, self.current_yaw)
        return None

    def run_calibration(self, cap: cv2.VideoCapture) -> bool:
        """Run 9-point calibration."""
        calibrator = GazeCalibrator(
            screen_width=self.screen_width,
            screen_height=self.screen_height,
            num_points=9,
            margin=0.15,
            samples_per_point=30,
            sample_delay=0.8,
        )

        def gaze_callback():
            ret, frame = cap.read()
            if not ret:
                return None
            frame = cv2.flip(frame, 1)
            self.process_frame(frame)
            return self.get_current_gaze()

        success = calibrator.run_calibration(gaze_callback)

        if success:
            self.mapper = calibrator.create_mapper(method='polynomial', degree=2)
            self.is_calibrated = True
        return success

    def save_calibration(self, filepath: str):
        if self.mapper:
            self.mapper.save(filepath)

    def load_calibration(self, filepath: str) -> bool:
        if os.path.exists(filepath):
            self.mapper = PolynomialMapper()
            self.mapper.load(filepath)
            self.is_calibrated = True
            return True
        return False


def draw_visualization(frame, tracker, screen_point):
    """Draw gaze visualization."""
    h, w = frame.shape[:2]

    # Draw face bbox and gaze arrow
    if tracker.current_bbox is not None:
        x_min, y_min, x_max, y_max = tracker.current_bbox
        cv2.rectangle(frame, (x_min, y_min), (x_max, y_max), (0, 255, 0), 2)

        if tracker.current_pitch is not None:
            cx, cy = (x_min + x_max) // 2, (y_min + y_max) // 2
            length = 100
            dx = -length * np.sin(tracker.current_yaw) * np.cos(tracker.current_pitch)
            dy = -length * np.sin(tracker.current_pitch)
            cv2.arrowedLine(frame, (cx, cy), (int(cx + dx), int(cy + dy)),
                           (0, 0, 255), 2, tipLength=0.3)

    # Draw minimap
    map_w, map_h = 200, 120
    map_x, map_y = w - map_w - 10, 10
    cv2.rectangle(frame, (map_x, map_y), (map_x + map_w, map_y + map_h), (50, 50, 50), -1)
    cv2.rectangle(frame, (map_x, map_y), (map_x + map_w, map_y + map_h), (200, 200, 200), 2)

    # Grid
    for i in range(1, 3):
        x = map_x + i * map_w // 3
        cv2.line(frame, (x, map_y), (x, map_y + map_h), (100, 100, 100), 1)
        y = map_y + i * map_h // 3
        cv2.line(frame, (map_x, y), (map_x + map_w, y), (100, 100, 100), 1)

    # Gaze point
    if screen_point is not None:
        dot_x = int(map_x + (screen_point[0] / tracker.screen_width) * map_w)
        dot_y = int(map_y + (screen_point[1] / tracker.screen_height) * map_h)
        cv2.circle(frame, (dot_x, dot_y), 6, (0, 0, 255), -1)
        cv2.circle(frame, (dot_x, dot_y), 8, (255, 255, 255), 2)

    # Info text
    y_off = map_y + map_h + 25
    if tracker.current_pitch is not None:
        cv2.putText(frame, f"Pitch: {np.degrees(tracker.current_pitch):+.1f}",
                   (map_x, y_off), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
        cv2.putText(frame, f"Yaw: {np.degrees(tracker.current_yaw):+.1f}",
                   (map_x, y_off + 18), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1)
    if screen_point is not None:
        cv2.putText(frame, f"Screen: ({int(screen_point[0])}, {int(screen_point[1])})",
                   (map_x, y_off + 40), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)

    return frame


def parse_args():
    parser = argparse.ArgumentParser(description='ONNX + Calibration demo')
    parser.add_argument('--model', type=str,
                       default=str(PROJECT_ROOT / 'weights' / 'L2CSNet_gaze360.onnx'),
                       help='ONNX model path')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--width', type=int, default=640)
    parser.add_argument('--height', type=int, default=480)
    parser.add_argument('--screen_width', type=int, default=1920)
    parser.add_argument('--screen_height', type=int, default=1080)
    parser.add_argument('--calibration', type=str, default='calibration_onnx.json')
    parser.add_argument('--cpu', action='store_true')
    parser.add_argument('--filter', type=str, default='balanced',
                       choices=['smooth', 'balanced', 'responsive', 'child', 'off'],
                       help='Filter preset: smooth, balanced, responsive, child, off')
    return parser.parse_args()


def main():
    args = parse_args()

    model_path = pathlib.Path(args.model)
    if not model_path.exists():
        print(f"Error: Model not found: {model_path}")
        print("Run: python convert_to_onnx.py")
        return

    tracker = ONNXCalibratedGazeTracker(
        str(model_path),
        screen_width=args.screen_width,
        screen_height=args.screen_height,
        use_gpu=not args.cpu
    )

    # Load existing calibration
    calib_path = PROJECT_ROOT / args.calibration
    if calib_path.exists():
        tracker.load_calibration(str(calib_path))
        print(f"Loaded calibration from {calib_path}")

    # Enable filter
    if args.filter != 'off':
        tracker.enable_filter(args.filter)
    else:
        tracker.disable_filter()

    cap = cv2.VideoCapture(args.camera)
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, args.width)
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, args.height)

    if not cap.isOpened():
        print("Cannot open camera")
        return

    print("\n=== Controls ===")
    print("'c' - Calibrate | 's' - Save | 'l' - Load | 'r' - Reset | 'q' - Quit")
    print("'f' - Toggle filter | '1' smooth | '2' balanced | '3' responsive | '4' child\n")

    fps_list = []

    while True:
        start = time.time()

        ret, frame = cap.read()
        if not ret:
            continue

        frame = cv2.flip(frame, 1)
        gaze, screen_point = tracker.process_frame(frame)
        frame = draw_visualization(frame, tracker, screen_point)

        # Status
        status = "CALIBRATED (ONNX)" if tracker.is_calibrated else "NOT CALIBRATED (press 'c')"
        color = (0, 255, 0) if tracker.is_calibrated else (0, 165, 255)
        cv2.putText(frame, status, (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)

        # Filter status
        filter_status = "Filter: ON" if tracker.filter_enabled else "Filter: OFF"
        filter_color = (0, 255, 0) if tracker.filter_enabled else (128, 128, 128)
        cv2.putText(frame, filter_status, (10, 50), cv2.FONT_HERSHEY_SIMPLEX, 0.5, filter_color, 1)

        # FPS
        fps = 1.0 / (time.time() - start)
        fps_list.append(fps)
        if len(fps_list) > 30:
            fps_list.pop(0)
        cv2.putText(frame, f"FPS: {sum(fps_list)/len(fps_list):.1f}",
                   (10, frame.shape[0] - 10), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 1)

        cv2.imshow("ONNX Calibrated Demo", frame)

        key = cv2.waitKey(1) & 0xFF
        if key == ord('q'):
            break
        elif key == ord('c'):
            cv2.destroyAllWindows()
            if tracker.run_calibration(cap):
                print("Calibration complete!")
        elif key == ord('s'):
            tracker.save_calibration(str(calib_path))
        elif key == ord('l'):
            if tracker.load_calibration(str(calib_path)):
                print("Calibration loaded!")
        elif key == ord('r'):
            tracker.mapper = None
            tracker.is_calibrated = False
            print("Calibration reset")
        elif key == ord('f'):
            # Toggle filter
            if tracker.filter_enabled:
                tracker.disable_filter()
            else:
                tracker.enable_filter("balanced")
        elif key == ord('1'):
            tracker.enable_filter("smooth")
        elif key == ord('2'):
            tracker.enable_filter("balanced")
        elif key == ord('3'):
            tracker.enable_filter("responsive")
        elif key == ord('4'):
            tracker.enable_filter("child")

    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
