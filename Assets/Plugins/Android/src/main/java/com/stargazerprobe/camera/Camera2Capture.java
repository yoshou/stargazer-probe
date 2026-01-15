package com.stargazerprobe.camera;

import android.app.Activity;
import android.content.Context;
import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.graphics.YuvImage;
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

import java.io.ByteArrayOutputStream;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;

public final class Camera2Capture {
    private static final String TAG = "Camera2Capture";

    private final Object frameLock = new Object();
    private byte[] latestJpeg;
    private long latestTimestampNs;

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
        stop();

        if (activity == null) {
            return false;
        }

        try {
            startBackgroundThread();

            CameraManager manager = (CameraManager) activity.getSystemService(Context.CAMERA_SERVICE);
            CameraSelection selection = chooseCamera(manager, useFront, targetWidth, targetHeight);
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

            imageReader = ImageReader.newInstance(selectedSize.getWidth(), selectedSize.getHeight(), ImageFormat.YUV_420_888, 2);
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
            latestJpeg = null;
            latestTimestampNs = 0L;
        }

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

    public byte[] consumeLatestJpeg() {
        synchronized (frameLock) {
            byte[] out = latestJpeg;
            latestJpeg = null;
            return out;
        }
    }

    public long getLatestTimestampNs() {
        synchronized (frameLock) {
            return latestTimestampNs;
        }
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
                        builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_VIDEO);

                        Range<Integer> fpsRange = chooseFpsRange(characteristics, targetFps);
                        if (fpsRange != null) {
                            builder.set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE, fpsRange);
                        }

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

            byte[] jpeg = yuv420ToJpeg(image, 85);
            if (jpeg == null || jpeg.length == 0) {
                return;
            }

            synchronized (frameLock) {
                latestJpeg = jpeg;
                latestTimestampNs = image.getTimestamp();
            }
        } catch (Exception e) {
            Log.e(TAG, "onImageAvailable failed", e);
        } finally {
            if (image != null) {
                try { image.close(); } catch (Exception ignored) {}
            }
        }
    }

    private static byte[] yuv420ToJpeg(Image image, int jpegQuality) {
        int width = image.getWidth();
        int height = image.getHeight();

        byte[] nv21 = yuv420ToNv21(image);
        if (nv21 == null) {
            return null;
        }

        YuvImage yuvImage = new YuvImage(nv21, ImageFormat.NV21, width, height, null);
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        boolean ok = yuvImage.compressToJpeg(new Rect(0, 0, width, height), jpegQuality, out);
        return ok ? out.toByteArray() : null;
    }

    private static byte[] yuv420ToNv21(Image image) {
        Image.Plane[] planes = image.getPlanes();
        if (planes == null || planes.length < 3) {
            return null;
        }

        int width = image.getWidth();
        int height = image.getHeight();

        int ySize = width * height;
        int uvSize = width * height / 2;
        byte[] out = new byte[ySize + uvSize];

        ByteBuffer yBuffer = planes[0].getBuffer();
        ByteBuffer uBuffer = planes[1].getBuffer();
        ByteBuffer vBuffer = planes[2].getBuffer();

        int yRowStride = planes[0].getRowStride();
        int yPixelStride = planes[0].getPixelStride();

        int uRowStride = planes[1].getRowStride();
        int uPixelStride = planes[1].getPixelStride();

        int vRowStride = planes[2].getRowStride();
        int vPixelStride = planes[2].getPixelStride();

        // Y plane
        int outIndex = 0;
        for (int row = 0; row < height; row++) {
            int yRowStart = row * yRowStride;
            if (yPixelStride == 1) {
                yBuffer.position(yRowStart);
                yBuffer.get(out, outIndex, width);
                outIndex += width;
            } else {
                for (int col = 0; col < width; col++) {
                    out[outIndex++] = yBuffer.get(yRowStart + col * yPixelStride);
                }
            }
        }

        // Interleave VU (NV21)
        int chromaHeight = height / 2;
        int chromaWidth = width / 2;
        int uvOutStart = ySize;
        for (int row = 0; row < chromaHeight; row++) {
            int uRowStart = row * uRowStride;
            int vRowStart = row * vRowStride;
            for (int col = 0; col < chromaWidth; col++) {
                int uIndex = uRowStart + col * uPixelStride;
                int vIndex = vRowStart + col * vPixelStride;

                int uvIndex = uvOutStart + row * width + col * 2;
                out[uvIndex] = vBuffer.get(vIndex);
                out[uvIndex + 1] = uBuffer.get(uIndex);
            }
        }

        return out;
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

                // Prefer ranges that include targetFps, otherwise closest upper bound.
                int score;
                if (lower <= targetFps && targetFps <= upper) {
                    score = (upper - targetFps) + (targetFps - lower);
                } else {
                    score = Math.min(Math.abs(upper - targetFps), Math.abs(lower - targetFps)) + 1000;
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

    private static CameraSelection chooseCamera(CameraManager manager, boolean useFront, int targetWidth, int targetHeight) throws CameraAccessException {
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

            Size[] sizes = map.getOutputSizes(ImageFormat.YUV_420_888);
            if (sizes == null || sizes.length == 0) {
                continue;
            }

            Size bestSize = chooseBestSize(sizes, targetWidth, targetHeight);
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

            Size[] sizes = map.getOutputSizes(ImageFormat.YUV_420_888);
            if (sizes == null || sizes.length == 0) continue;

            Size bestSize = chooseBestSize(sizes, targetWidth, targetHeight);
            if (bestSize == null) continue;

            Integer facing = cc.get(CameraCharacteristics.LENS_FACING);
            boolean isFront = facing != null && facing == CameraCharacteristics.LENS_FACING_FRONT;

            return new CameraSelection(id, bestSize, cc, isFront);
        }

        return null;
    }

    private static Size chooseBestSize(Size[] sizes, int targetWidth, int targetHeight) {
        if (sizes == null || sizes.length == 0) {
            return null;
        }

        final float targetAspect = targetHeight > 0 ? (float) targetWidth / (float) targetHeight : 0f;

        Size best = null;
        float bestScore = Float.MAX_VALUE;

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
