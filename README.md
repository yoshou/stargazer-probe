# Stargazer Probe

A Unity project that collects **camera frames + IMU sensor data** (accelerometer, gyroscope, magnetometer) on an Android device and streams them to a PC via **gRPC (duplex streaming)**.

It connects to a configurable gRPC endpoint and continuously sends captured camera frames along with the latest available IMU sample.

## Requirements

- Unity 6: `6000.0.45f1`
- Android device with camera + IMU sensors
- A gRPC server on the same network (HTTP/2)
- Unity Input System (package: `com.unity.inputsystem`)

> Note: This project uses `Grpc.Net.Client` for the gRPC client. On Android, it uses `YetAnotherHttpHandler` for more reliable HTTP/2 support.

## Setup

### 1. Clone the Repository

```bash
git clone <repository-url>
cd stargazer-probe
```

### 2. Install / Restore Required Packages

#### TextMesh Pro

This project uses TextMesh Pro for UI. If the required resources are not imported on first launch, run:

- `Window > TextMeshPro > Import TMP Essential Resources`

#### NuGet Packages (NuGet for Unity)

This project uses NuGet packages (gRPC / Protobuf, etc.). Restore them via NuGet for Unity based on `Assets/packages.config`:

- `NuGet > Restore Packages`

Main packages:
- `Google.Protobuf (3.33.3)`
- `Grpc.Net.Client (2.76.0)`
- `Grpc.Core.Api (2.76.0)`

#### YetAnotherHttpHandler (UPM)

`YetAnotherHttpHandler` is installed via UPM for Android HTTP/2 support (see [Packages/manifest.json](Packages/manifest.json)). It should be fetched automatically.

### 3. Configure Server Endpoint

You can configure the server endpoint in the in-app Settings UI (stored in `PlayerPrefs`). Default values:

- IP: `192.168.1.100`
- Port: `50051`

To reset settings from the Unity Editor menu:

- `Tools > Reset PlayerPrefs to Defaults`

## Build

1. Open the project in Unity
2. Open the scene: `Assets/Scenes/SampleScene.unity`
3. Go to `File > Build Settings...`, switch Platform to Android, then Build / Build And Run

## Usage

1. Launch the app on your Android device
2. Press `Start`
   - Starts camera capture
   - Starts the gRPC stream and sends camera frames + the latest IMU sample
3. Optionally adjust settings via `Settings`
   - Server: IP / Port
   - Camera: Resolution / FPS / JPEG Quality
   - Sensor: Sampling Rate / Enable toggles (Accel/Gyro/Mag)
4. Press `Stop` to stop streaming and capture

## License

This repository currently does not include a `LICENSE` file.

- If you want to clearly define the license for this project, add a `LICENSE` file.
- Third-party components (Unity / NuGet packages / YetAnotherHttpHandler, etc.) are subject to their respective licenses.
