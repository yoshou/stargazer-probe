package com.stargazerprobe.camera;

import android.app.Activity;
import android.content.Context;
import android.graphics.ImageFormat;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;
import android.util.Range;
import android.util.Size;
import android.util.SizeF;
import android.view.Surface;
import android.view.WindowManager;

import java.util.concurrent.atomic.AtomicBoolean;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.ArrayDeque;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

public final class Camera2Capture {
    private static final String TAG = "Camera2Capture";

    // Output format: we want encoded JPEG bytes directly.
    private static final int OUTPUT_IMAGE_FORMAT = ImageFormat.JPEG;

    // Image processing guard: keep only one conversion active to avoid callback backlog.
    private final AtomicBoolean isProcessingImage = new AtomicBoolean(false);

    private final Object frameLock = new Object();
    // FIFO queue of encoded JPEG frames to avoid drops caused by polling.
    // Unity will drain this queue via consumeNextJpeg().
    private final ArrayDeque<byte[]> jpegQueue = new ArrayDeque<>();
    private int maxQueuedFrames = 32; // ~1s at 30fps

    // Camera configuration
    private int jpegQuality = 75;
    private int targetFpsRequested = 30;

    // Diagnostics
    private Range<Integer> activeFpsRange;
    private long fpsWindowStartNs;
    private int fpsWindowFrames;
    private long fpsWindowProcNs;

    private boolean isFrontFacing;
    private int sensorOrientation;

    private String cameraId;
    private Size selectedSize;
    private CameraCharacteristics characteristics;

    private HandlerThread backgroundThread;
    private Handler backgroundHandler;

    private CameraDevice cameraDevice;
    private CameraCaptureSession captureSession;
    private ImageReader imageReader;

    public boolean start(Activity activity, int targetWidth, int targetHeight, int targetFps, boolean useFront) {
        return start(activity, targetWidth, targetHeight, targetFps, useFront, 75);
    }

    public boolean start(Activity activity, int targetWidth, int targetHeight, int targetFps, boolean useFront, int jpegQuality) {
        stop();

        if (activity == null) {
            return false;
        }

        this.jpegQuality = Math.max(1, Math.min(100, jpegQuality));
        this.targetFpsRequested = Math.max(1, targetFps);

        try {
            startBackgroundThread();

            CameraManager manager = (CameraManager) activity.getSystemService(Context.CAMERA_SERVICE);
            CameraSelection selection = chooseCamera(manager, useFront, targetWidth, targetHeight, targetFps);
            if (selection == null) {
                Log.e(TAG, "No suitable camera found");
                stop();
                return false;
            }

            cameraId = selection.cameraId;
            selectedSize = selection.size;
            characteristics = selection.characteristics;
            isFrontFacing = selection.isFrontFacing;
            Integer so = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION);
            sensorOrientation = so != null ? so : 0;

            resetDiagnostics();

            int w = selectedSize.getWidth();
            int h = selectedSize.getHeight();

            // Keep a slightly deeper queue to reduce stalls if conversion is momentarily slow.
            imageReader = ImageReader.newInstance(w, h, OUTPUT_IMAGE_FORMAT, 4);
            imageReader.setOnImageAvailableListener(reader -> onImageAvailable(reader), backgroundHandler);

            manager.openCamera(cameraId, new CameraDevice.StateCallback() {
                @Override
                public void onOpened(CameraDevice camera) {
                    cameraDevice = camera;
                    createSession(targetFps);
                }

                @Override
                public void onDisconnected(CameraDevice camera) {
                    try { camera.close(); } catch (Exception ignored) {}
                    cameraDevice = null;
                }

                @Override
                public void onError(CameraDevice camera, int error) {
                    Log.e(TAG, "CameraDevice error=" + error);
                    try { camera.close(); } catch (Exception ignored) {}
                    cameraDevice = null;
                }
            }, backgroundHandler);

            return true;
        } catch (SecurityException se) {
            Log.e(TAG, "Missing CAMERA permission", se);
            stop();
            return false;
        } catch (Exception e) {
            Log.e(TAG, "start failed", e);
            stop();
            return false;
        }
    }

    public void stop() {
        synchronized (frameLock) {
            jpegQueue.clear();
        }

        isProcessingImage.set(false);
        resetDiagnostics();

        try {
            if (captureSession != null) {
                try { captureSession.stopRepeating(); } catch (Exception ignored) {}
                try { captureSession.close(); } catch (Exception ignored) {}
            }
        } finally {
            captureSession = null;
        }

        try {
            if (cameraDevice != null) {
                try { cameraDevice.close(); } catch (Exception ignored) {}
            }
        } finally {
            cameraDevice = null;
        }

        try {
            if (imageReader != null) {
                try { imageReader.close(); } catch (Exception ignored) {}
            }
        } finally {
            imageReader = null;
        }

        stopBackgroundThread();

        cameraId = null;
        selectedSize = null;
        characteristics = null;
        isFrontFacing = false;
        sensorOrientation = 0;
    }

    public byte[] consumeNextJpeg() {
        synchronized (frameLock) {
            if (jpegQueue.isEmpty()) {
                return null;
            }
            return jpegQueue.removeFirst();
        }
    }

    public int getWidth() {
        return selectedSize != null ? selectedSize.getWidth() : 0;
    }

    public int getHeight() {
        return selectedSize != null ? selectedSize.getHeight() : 0;
    }

    public int getJpegQuality() {
        return jpegQuality;
    }

    public boolean getIsFrontFacing() {
        return isFrontFacing;
    }

    public int getRotationDegrees(Activity activity) {
        if (activity == null) {
            return 0;
        }

        int deviceRotation = getDeviceRotationDegrees(activity);

        // Degrees needed to rotate the camera buffer to match the current device orientation.
        // (UI layer can apply -rotation for Unity's coordinate convention.)
        if (isFrontFacing) {
            return (sensorOrientation + deviceRotation) % 360;
        }

        return (sensorOrientation - deviceRotation + 360) % 360;
    }

    public float[] getIntrinsics() {
        if (characteristics == null || selectedSize == null) {
            return null;
        }

        try {
            float[] focalLengths = characteristics.get(CameraCharacteristics.LENS_INFO_AVAILABLE_FOCAL_LENGTHS);
            SizeF physicalSize = characteristics.get(CameraCharacteristics.SENSOR_INFO_PHYSICAL_SIZE);
            android.graphics.Rect activeArray = characteristics.get(CameraCharacteristics.SENSOR_INFO_ACTIVE_ARRAY_SIZE);

            if (focalLengths == null || focalLengths.length == 0 || physicalSize == null || activeArray == null) {
                return null;
            }

            float focalMm = focalLengths[0];

            float pxPerMmX = (float) activeArray.width() / physicalSize.getWidth();
            float pxPerMmY = (float) activeArray.height() / physicalSize.getHeight();

            float fxSensor = focalMm * pxPerMmX;
            float fySensor = focalMm * pxPerMmY;

            float scaleX = (float) selectedSize.getWidth() / (float) activeArray.width();
            float scaleY = (float) selectedSize.getHeight() / (float) activeArray.height();
            float fx = fxSensor * scaleX;
            float fy = fySensor * scaleY;

            float cx = (activeArray.width() / 2.0f) * scaleX;
            float cy = (activeArray.height() / 2.0f) * scaleY;

            return new float[]{fx, fy, cx, cy};
        } catch (Exception e) {
            Log.w(TAG, "getIntrinsics failed", e);
            return null;
        }
    }

    private void createSession(int targetFps) {
        if (cameraDevice == null || imageReader == null) {
            return;
        }

        try {
            List<Surface> surfaces = Collections.singletonList(imageReader.getSurface());

            cameraDevice.createCaptureSession(surfaces, new CameraCaptureSession.StateCallback() {
                @Override
                public void onConfigured(CameraCaptureSession session) {
                    captureSession = session;

                    try {
                        CaptureRequest.Builder builder = cameraDevice.createCaptureRequest(CameraDevice.TEMPLATE_RECORD);
                        builder.addTarget(imageReader.getSurface());

                        configureCaptureRequest(builder, targetFps);

                        session.setRepeatingRequest(builder.build(), null, backgroundHandler);
                    } catch (Exception e) {
                        Log.e(TAG, "setRepeatingRequest failed", e);
                    }
                }

                @Override
                public void onConfigureFailed(CameraCaptureSession session) {
                    Log.e(TAG, "createCaptureSession configure failed");
                }
            }, backgroundHandler);
        } catch (Exception e) {
            Log.e(TAG, "createSession failed", e);
        }
    }

    private void onImageAvailable(ImageReader reader) {
        Image image = null;
        try {
            image = reader.acquireLatestImage();
            if (image == null) {
                return;
            }

            // If a previous frame is still being converted, drop this one quickly.
            if (!isProcessingImage.compareAndSet(false, true)) {
                return;
            }

            long procStartNs = System.nanoTime();
            byte[] jpeg = extractJpegBytes(image);
            long procEndNs = System.nanoTime();
            if (jpeg == null || jpeg.length == 0) {
                return;
            }

            synchronized (frameLock) {
                // Bound queue to avoid unbounded memory growth.
                // If consumer falls behind, drop oldest frames rather than OOM.
                while (jpegQueue.size() >= Math.max(1, maxQueuedFrames)) {
                    jpegQueue.removeFirst();
                }

                jpegQueue.addLast(jpeg);
            }

            updateFpsDiagnostics(image.getTimestamp(), procEndNs - procStartNs);
        } catch (Exception e) {
            Log.e(TAG, "onImageAvailable failed", e);
        } finally {
            if (image != null) {
                try { image.close(); } catch (Exception ignored) {}
            }

            isProcessingImage.set(false);
        }
    }

    private void resetDiagnostics() {
        activeFpsRange = null;
        fpsWindowStartNs = 0L;
        fpsWindowFrames = 0;
        fpsWindowProcNs = 0L;
    }

    private void configureCaptureRequest(CaptureRequest.Builder builder, int targetFps) {
        // JPEG quality is only applicable when output is JPEG.
        // Use the quality setting (1-100). Camera2 expects a Byte.
        try {
            builder.set(CaptureRequest.JPEG_QUALITY, (byte) Math.max(1, Math.min(100, jpegQuality)));
        } catch (Exception ignored) {
            // Some devices may ignore or reject this key in certain templates.
        }

        // Prefer performance-oriented defaults for realtime capture.
        builder.set(CaptureRequest.CONTROL_MODE, CaptureRequest.CONTROL_MODE_AUTO);
        builder.set(CaptureRequest.CONTROL_AE_MODE, CaptureRequest.CONTROL_AE_MODE_ON);
        builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_VIDEO);
        builder.set(CaptureRequest.CONTROL_AWB_MODE, CaptureRequest.CONTROL_AWB_MODE_AUTO);
        builder.set(CaptureRequest.NOISE_REDUCTION_MODE, CaptureRequest.NOISE_REDUCTION_MODE_FAST);
        builder.set(CaptureRequest.EDGE_MODE, CaptureRequest.EDGE_MODE_FAST);
        builder.set(CaptureRequest.CONTROL_VIDEO_STABILIZATION_MODE, CaptureRequest.CONTROL_VIDEO_STABILIZATION_MODE_OFF);

        Range<Integer> fpsRange = chooseFpsRange(characteristics, targetFps);
        if (fpsRange != null) {
            builder.set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, fpsRange);
            activeFpsRange = fpsRange;
        }
    }

    private void updateFpsDiagnostics(long timestampNs, long processingTimeNs) {
        if (timestampNs <= 0) {
            return;
        }

        if (fpsWindowStartNs == 0L) {
            fpsWindowStartNs = timestampNs;
            fpsWindowFrames = 0;
            fpsWindowProcNs = 0L;
        }

        fpsWindowFrames++;
        fpsWindowProcNs += Math.max(0L, processingTimeNs);
        long dt = timestampNs - fpsWindowStartNs;

        if (dt >= 1_000_000_000L) {
            double fps = (fpsWindowFrames * 1_000_000_000.0) / (double) dt;
            double avgProcMs = fpsWindowFrames > 0 ? (fpsWindowProcNs / 1_000_000.0) / (double) fpsWindowFrames : 0.0;

            Log.i(TAG, "Camera2 incomingFPS=" + String.format("%.1f", fps)
                    + " size=" + (selectedSize != null ? (selectedSize.getWidth() + "x" + selectedSize.getHeight()) : "?")
                    + " aeFpsRange=" + (activeFpsRange != null ? activeFpsRange.toString() : "null")
                    + " jpegQ=" + jpegQuality
                    + " avgProcMs=" + String.format("%.2f", avgProcMs));

            fpsWindowStartNs = timestampNs;
            fpsWindowFrames = 0;
            fpsWindowProcNs = 0L;
        }
    }

    private static byte[] extractJpegBytes(Image image) {
        if (image == null) {
            return null;
        }

        try {
            Image.Plane[] planes = image.getPlanes();
            if (planes == null || planes.length < 1) {
                return null;
            }

            ByteBuffer buffer = planes[0].getBuffer();
            if (buffer == null) {
                return null;
            }

            int length = buffer.remaining();
            if (length <= 0) {
                return null;
            }

            byte[] jpeg = new byte[length];
            buffer.get(jpeg);
            return jpeg;
        } catch (Exception ignored) {
            return null;
        }
    }

    private static int getDeviceRotationDegrees(Activity activity) {
        try {
            WindowManager wm = (WindowManager) activity.getSystemService(Context.WINDOW_SERVICE);
            if (wm == null || wm.getDefaultDisplay() == null) {
                return 0;
            }

            int rotation = wm.getDefaultDisplay().getRotation();
            switch (rotation) {
                case Surface.ROTATION_0:
                    return 0;
                case Surface.ROTATION_90:
                    return 90;
                case Surface.ROTATION_180:
                    return 180;
                case Surface.ROTATION_270:
                    return 270;
                default:
                    return 0;
            }
        } catch (Exception ignored) {
            return 0;
        }
    }

    private static Range<Integer> chooseFpsRange(CameraCharacteristics characteristics, int targetFps) {
        if (characteristics == null) {
            return null;
        }

        try {
            Range<Integer>[] ranges = characteristics.get(CameraCharacteristics.CONTROL_AE_AVAILABLE_TARGET_FPS_RANGES);
            if (ranges == null || ranges.length == 0) {
                return null;
            }

            Range<Integer> best = null;
            int bestScore = Integer.MAX_VALUE;
            for (Range<Integer> r : ranges) {
                if (r == null) continue;

                int lower = r.getLower();
                int upper = r.getUpper();

                // Prefer fixed/near-fixed ranges at targetFps (e.g., 30-30).
                // Wide ranges like 15-30 often allow the camera to drop frames in low light.
                int score;
                if (lower <= targetFps && targetFps <= upper) {
                    int lowerPenalty = (targetFps - lower) * 10;
                    int upperPenalty = (upper - targetFps);
                    score = lowerPenalty + upperPenalty;
                } else {
                    score = Math.min(Math.abs(upper - targetFps), Math.abs(lower - targetFps)) + 10_000;
                }

                if (score < bestScore) {
                    bestScore = score;
                    best = r;
                }
            }

            return best;
        } catch (Exception ignored) {
            return null;
        }
    }

    private static CameraSelection chooseCamera(CameraManager manager, boolean useFront, int targetWidth, int targetHeight, int targetFps) throws CameraAccessException {
        if (manager == null) {
            return null;
        }

        String[] ids = manager.getCameraIdList();
        if (ids == null || ids.length == 0) {
            return null;
        }

        List<CameraSelection> candidates = new ArrayList<>();

        for (String id : ids) {
            if (id == null) continue;

            CameraCharacteristics cc = manager.getCameraCharacteristics(id);
            Integer facing = cc.get(CameraCharacteristics.LENS_FACING);
            boolean isFront = facing != null && facing == CameraCharacteristics.LENS_FACING_FRONT;

            if (useFront != isFront) {
                continue;
            }

            StreamConfigurationMap map = cc.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
            if (map == null) {
                continue;
            }

            Size[] sizes = map.getOutputSizes(OUTPUT_IMAGE_FORMAT);
            if (sizes == null || sizes.length == 0) {
                continue;
            }

            Size bestSize = chooseBestSize(map, sizes, targetWidth, targetHeight, targetFps);
            if (bestSize == null) {
                continue;
            }

            candidates.add(new CameraSelection(id, bestSize, cc, isFront));
        }

        if (!candidates.isEmpty()) {
            // If front requested and found, or back requested and found, return first (already bestSize per camera).
            return candidates.get(0);
        }

        // Fallback: try any camera if requested facing not found
        for (String id : ids) {
            if (id == null) continue;
            CameraCharacteristics cc = manager.getCameraCharacteristics(id);
            StreamConfigurationMap map = cc.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
            if (map == null) continue;

            Size[] sizes = map.getOutputSizes(OUTPUT_IMAGE_FORMAT);
            if (sizes == null || sizes.length == 0) continue;

            Size bestSize = chooseBestSize(map, sizes, targetWidth, targetHeight, targetFps);
            if (bestSize == null) continue;

            Integer facing = cc.get(CameraCharacteristics.LENS_FACING);
            boolean isFront = facing != null && facing == CameraCharacteristics.LENS_FACING_FRONT;

            return new CameraSelection(id, bestSize, cc, isFront);
        }

        return null;
    }

    private static Size chooseBestSize(StreamConfigurationMap map, Size[] sizes, int targetWidth, int targetHeight, int targetFps) {
        if (sizes == null || sizes.length == 0) {
            return null;
        }

        // Prefer sizes that can actually reach targetFps (based on min frame duration).
        // If the camera doesn't report durations, we keep all sizes.
        final long targetFrameNs = targetFps > 0 ? (1_000_000_000L / (long) targetFps) : 0L;

        final float targetAspect = targetHeight > 0 ? (float) targetWidth / (float) targetHeight : 0f;

        Size best = null;
        float bestScore = Float.MAX_VALUE;

        for (Size s : sizes) {
            if (s == null) continue;

            if (map != null && targetFrameNs > 0L) {
                try {
                    long minFrameDuration = map.getOutputMinFrameDuration(OUTPUT_IMAGE_FORMAT, s);
                    if (minFrameDuration > 0L && minFrameDuration > targetFrameNs) {
                        // Cannot meet target FPS at this resolution.
                        continue;
                    }
                } catch (Exception ignored) {
                    // Keep candidate if the duration query fails.
                }
            }

            int w = s.getWidth();
            int h = s.getHeight();

            float aspect = h > 0 ? (float) w / (float) h : 0f;
            float aspectPenalty = Math.abs(aspect - targetAspect) * 1000f;

            float sizePenalty = Math.abs(w - targetWidth) + Math.abs(h - targetHeight);

            float score = aspectPenalty + sizePenalty;
            if (score < bestScore) {
                bestScore = score;
                best = s;
            }
        }

        // If all sizes were filtered out by minFrameDuration, fall back to original behavior.
        if (best != null) {
            return best;
        }

        // Fallback: closest size ignoring FPS feasibility.
        for (Size s : sizes) {
            if (s == null) continue;

            int w = s.getWidth();
            int h = s.getHeight();

            float aspect = h > 0 ? (float) w / (float) h : 0f;
            float aspectPenalty = Math.abs(aspect - targetAspect) * 1000f;
            float sizePenalty = Math.abs(w - targetWidth) + Math.abs(h - targetHeight);

            float score = aspectPenalty + sizePenalty;
            if (score < bestScore) {
                bestScore = score;
                best = s;
            }
        }

        return best != null ? best : sizes[0];
    }

    private void startBackgroundThread() {
        backgroundThread = new HandlerThread("Camera2Capture");
        backgroundThread.start();
        backgroundHandler = new Handler(backgroundThread.getLooper());
    }

    private void stopBackgroundThread() {
        try {
            if (backgroundThread != null) {
                backgroundThread.quitSafely();
                try {
                    backgroundThread.join(400);
                } catch (InterruptedException ignored) {
                }
            }
        } finally {
            backgroundThread = null;
            backgroundHandler = null;
        }
    }

    private static final class CameraSelection {
        public final String cameraId;
        public final Size size;
        public final CameraCharacteristics characteristics;
        public final boolean isFrontFacing;

        public CameraSelection(String cameraId, Size size, CameraCharacteristics characteristics, boolean isFrontFacing) {
            this.cameraId = cameraId;
            this.size = size;
            this.characteristics = characteristics;
            this.isFrontFacing = isFrontFacing;
        }
    }
}
