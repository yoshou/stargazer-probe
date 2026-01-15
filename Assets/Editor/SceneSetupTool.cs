using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using StargazerProbe.UI;
using StargazerProbe.Sensors;
using StargazerProbe.Camera;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace StargazerProbe.Editor
{
    /// <summary>
    /// Editor script to automatically generate UI structure in the scene
    /// </summary>
    public class SceneSetupTool : EditorWindow
    {
        [MenuItem("Stargazer/Setup Scene")]
        public static void SetupScene()
        {
            if (EditorUtility.DisplayDialog("Setup Scene", 
                "This will delete existing UI and recreate the scene structure. Continue?", 
                "Yes", "No"))
            {
                CleanupExistingObjects();
                CreateSceneStructure();
                EditorUtility.DisplayDialog("Complete", "Scene setup completed!", "OK");
            }
        }

        private static void CleanupExistingObjects()
        {
            // Delete all Canvas objects
            Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            foreach (Canvas canvas in canvases)
            {
                Object.DestroyImmediate(canvas.gameObject);
            }
            
            // Delete all GameObjects with specific components
            IMUSensorManager[] sensorManagers = Object.FindObjectsByType<IMUSensorManager>(FindObjectsSortMode.None);
            foreach (var comp in sensorManagers)
            {
                Object.DestroyImmediate(comp.gameObject);
            }
            
            // Delete ICameraCapture implementations
            MobileCameraCapture[] mobileCaptures = Object.FindObjectsByType<MobileCameraCapture>(FindObjectsSortMode.None);
            foreach (var comp in mobileCaptures)
            {
                Object.DestroyImmediate(comp.gameObject);
            }

            Camera2CameraCapture[] camera2Captures = Object.FindObjectsByType<Camera2CameraCapture>(FindObjectsSortMode.None);
            foreach (var comp in camera2Captures)
            {
                Object.DestroyImmediate(comp.gameObject);
            }
            
            UIManager[] uiManagers = Object.FindObjectsByType<UIManager>(FindObjectsSortMode.None);
            foreach (var comp in uiManagers)
            {
                Object.DestroyImmediate(comp.gameObject);
            }
            
            Debug.Log("Cleanup completed");
        }

        private static void CreateSceneStructure()
        {
            // 0. EventSystem (required for UI)
            CreateEventSystem();
            
            // Setup Main Camera (Black Background)
            SetupMainCamera();
            
            // 1. Managers
            GameObject sensorManager = CreateSensorManager();
            GameObject cameraCapture = CreateCameraCapture();
            
            // 2. Canvas
            GameObject canvas = CreateCanvas();
            
            // 3. UI Elements
            GameObject cameraPreview = CreateCameraPreview(canvas);
            
            // Integrated Panel (Control, Status, Sensors)
            GameObject controlPanel = CreateControlPanel(canvas);
            
            GameObject settingsPanel = CreateSettingsPanel(canvas);
            
            // 4. Wire up SettingsPanel references
            SettingsPanel settingsPanelComp = settingsPanel.GetComponent<SettingsPanel>();
            SerializedObject spSo = new SerializedObject(settingsPanelComp);
            spSo.FindProperty("sensorManager").objectReferenceValue = sensorManager.GetComponent<IMUSensorManager>();
            // Get ICameraCapture (MobileCameraCapture or Camera2CameraCapture)
            Component cameraCaptureComp = cameraCapture.GetComponent<MobileCameraCapture>() as Component
                ?? cameraCapture.GetComponent<Camera2CameraCapture>() as Component;
            spSo.FindProperty("cameraCapture").objectReferenceValue = cameraCaptureComp;
            spSo.ApplyModifiedProperties();
            
            // 5. UIManager and wire up references
            GameObject uiManager = CreateUIManager(
                sensorManager.GetComponent<IMUSensorManager>(),
                cameraPreview.transform.Find("PreviewContainer/PreviewImage").GetComponent<RawImage>(),
                controlPanel,
                controlPanel,
                controlPanel.transform.Find("ButtonsPanel/StartStopButton").GetComponent<Button>(),
                controlPanel.transform.Find("ButtonsPanel/SettingsButton").GetComponent<Button>(),
                settingsPanel
            );

            // 6. Responsive layout (portrait/landscape)
            ResponsiveUILayout responsive = canvas.AddComponent<ResponsiveUILayout>();
            SerializedObject rlSo = new SerializedObject(responsive);
            rlSo.FindProperty("cameraPreviewPanel").objectReferenceValue = cameraPreview.GetComponent<RectTransform>();
            rlSo.FindProperty("controlPanel").objectReferenceValue = controlPanel.GetComponent<RectTransform>();
            rlSo.FindProperty("buttonsPanel").objectReferenceValue = controlPanel.transform.Find("ButtonsPanel").GetComponent<RectTransform>();
            rlSo.FindProperty("infoPanel").objectReferenceValue = controlPanel.transform.Find("InfoPanel").GetComponent<RectTransform>();
            rlSo.FindProperty("connectionStatus").objectReferenceValue = controlPanel.transform.Find("InfoPanel/ConnectionStatus").GetComponent<RectTransform>();
            rlSo.FindProperty("fpsCounter").objectReferenceValue = controlPanel.transform.Find("InfoPanel/FPSCounter").GetComponent<RectTransform>();
            rlSo.FindProperty("queueSize").objectReferenceValue = controlPanel.transform.Find("InfoPanel/QueueSize").GetComponent<RectTransform>();
            rlSo.FindProperty("accelText").objectReferenceValue = controlPanel.transform.Find("InfoPanel/AccelText").GetComponent<RectTransform>();
            rlSo.FindProperty("gyroText").objectReferenceValue = controlPanel.transform.Find("InfoPanel/GyroText").GetComponent<RectTransform>();
            rlSo.FindProperty("magText").objectReferenceValue = controlPanel.transform.Find("InfoPanel/MagText").GetComponent<RectTransform>();
            rlSo.ApplyModifiedProperties();
            
            Debug.Log("Scene setup completed!");
        }

        private static void CreateEventSystem()
        {
            // Delete existing EventSystem if any
            UnityEngine.EventSystems.EventSystem existingEventSystem = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (existingEventSystem != null)
            {
                Object.DestroyImmediate(existingEventSystem.gameObject);
            }
            
            GameObject eventSystemGO = new GameObject("EventSystem");
            eventSystemGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemGO.AddComponent<InputSystemUIInputModule>();
            
            Debug.Log("EventSystem created with InputSystemUIInputModule");
        }

        private static void SetupMainCamera()
        {
            UnityEngine.Camera mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                // Try finding by tag explicitly if .main fails (though .main uses tag)
                GameObject camGO = GameObject.FindWithTag("MainCamera");
                if (camGO != null)
                {
                    mainCam = camGO.GetComponent<UnityEngine.Camera>();
                }
                
                if (mainCam == null)
                {
                    camGO = new GameObject("Main Camera");
                    camGO.tag = "MainCamera";
                    mainCam = camGO.AddComponent<UnityEngine.Camera>();
                    camGO.AddComponent<AudioListener>();
                }
            }

            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            
            // Reset position for consistency
            mainCam.transform.position = new Vector3(0, 0, -10f);
            mainCam.transform.rotation = Quaternion.identity;
            
            Debug.Log("Main Camera configured with black background");
        }

        private static GameObject CreateSensorManager()
        {
            GameObject go = new GameObject("SensorManager");
            go.AddComponent<IMUSensorManager>();
            return go;
        }

        private static GameObject CreateCameraCapture()
        {
            GameObject go = new GameObject("CameraCapture");
            // CameraCaptureFactoryを使用して適切な実装を追加
            CameraCaptureFactory.CreateCameraCapture(go);
            return go;
        }

        private static GameObject CreateCanvas()
        {
            GameObject canvasGO = new GameObject("Canvas");
            Canvas canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2220); // Pixel 3a
            scaler.matchWidthOrHeight = 0.5f;
            
            canvasGO.AddComponent<GraphicRaycaster>();
            
            return canvasGO;
        }

        private static GameObject CreateCameraPreview(GameObject canvas)
        {
            GameObject panel = new GameObject("CameraPreviewPanel");
            panel.transform.SetParent(canvas.transform, false);
            
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.offsetMin = new Vector2(0, 320); // Default: portrait control height (will be overridden in ResponsiveUILayout)
            rt.offsetMax = Vector2.zero;
            
            // Preview container keeps a stable box inside the panel.
            GameObject container = new GameObject("PreviewContainer");
            container.transform.SetParent(panel.transform, false);
            RectTransform containerRt = container.AddComponent<RectTransform>();
            containerRt.anchorMin = Vector2.zero;
            containerRt.anchorMax = Vector2.one;
            containerRt.offsetMin = Vector2.zero;
            containerRt.offsetMax = Vector2.zero;

            GameObject image = new GameObject("PreviewImage");
            image.transform.SetParent(container.transform, false);
            RawImage rawImage = image.AddComponent<RawImage>();
            rawImage.color = new Color(0.2f, 0.2f, 0.2f);

            RectTransform imgRt = image.GetComponent<RectTransform>();
            imgRt.anchorMin = Vector2.zero;
            imgRt.anchorMax = Vector2.one;
            imgRt.offsetMin = Vector2.zero;
            imgRt.offsetMax = Vector2.zero;
            
            return panel;
        }

        private static GameObject CreateControlPanel(GameObject canvas)
        {
            GameObject panel = new GameObject("ControlPanel");
            panel.transform.SetParent(canvas.transform, false);
            
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 0);
            rt.sizeDelta = new Vector2(0, 320); // Default: portrait height (landscape will be overridden)
            
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // --- Info Panel (default: portrait top) ---
            GameObject infoPanel = new GameObject("InfoPanel");
            infoPanel.transform.SetParent(panel.transform, false);
            RectTransform infoRt = infoPanel.AddComponent<RectTransform>();
            infoRt.anchorMin = new Vector2(0, 1);
            infoRt.anchorMax = new Vector2(1, 1);
            infoRt.pivot = new Vector2(0.5f, 1);
            infoRt.anchoredPosition = Vector2.zero;
            infoRt.sizeDelta = new Vector2(0, 150);

            CreateTextTMP(infoPanel, "ConnectionStatus", "Disconnected", new Vector2(30, -10), new Vector2(320, 40), TextAlignmentOptions.Left, 32);
            CreateTextTMP(infoPanel, "FPSCounter", "FPS: 0", new Vector2(360, -10), new Vector2(220, 40), TextAlignmentOptions.Left, 32);
            CreateTextTMP(infoPanel, "QueueSize", "Queue: 0", new Vector2(600, -10), new Vector2(260, 40), TextAlignmentOptions.Left, 32);

            float startY = -55;
            float stepY = 36;
            CreateTextTMP(infoPanel, "AccelText", "Accel: 0.00, 0.00, 0.00", new Vector2(30, startY), new Vector2(1000, 40), TextAlignmentOptions.Left, 34);
            CreateTextTMP(infoPanel, "GyroText", "Gyro: 0.00, 0.00, 0.00", new Vector2(30, startY - stepY), new Vector2(1000, 40), TextAlignmentOptions.Left, 34);
            CreateTextTMP(infoPanel, "MagText", "Mag: 0.0, 0.0, 0.0", new Vector2(30, startY - stepY * 2), new Vector2(1000, 40), TextAlignmentOptions.Left, 34);

            // --- Buttons Panel (default: portrait bottom) ---
            GameObject buttonsPanel = new GameObject("ButtonsPanel");
            buttonsPanel.transform.SetParent(panel.transform, false);
            RectTransform buttonsRt = buttonsPanel.AddComponent<RectTransform>();
            buttonsRt.anchorMin = new Vector2(0, 0);
            buttonsRt.anchorMax = new Vector2(1, 0);
            buttonsRt.pivot = new Vector2(0.5f, 0);
            buttonsRt.anchoredPosition = Vector2.zero;
            buttonsRt.sizeDelta = new Vector2(0, 170);

            GameObject settingsBtn = CreateButton(buttonsPanel, "SettingsButton", "Settings",
                Vector2.zero, new Vector2(360, 150), new Color(0.3f, 0.3f, 0.4f), 52);

            GameObject startStopBtn = CreateButton(buttonsPanel, "StartStopButton", "START",
                Vector2.zero, new Vector2(360, 150), new Color(0.2f, 0.8f, 0.2f), 52);

            // Center the two buttons as a pair
            RectTransform settingsRt = settingsBtn.GetComponent<RectTransform>();
            settingsRt.anchorMin = new Vector2(0.5f, 0.5f);
            settingsRt.anchorMax = new Vector2(0.5f, 0.5f);
            settingsRt.pivot = new Vector2(0.5f, 0.5f);
            settingsRt.anchoredPosition = new Vector2(-195, 0);

            RectTransform startStopRt = startStopBtn.GetComponent<RectTransform>();
            startStopRt.anchorMin = new Vector2(0.5f, 0.5f);
            startStopRt.anchorMax = new Vector2(0.5f, 0.5f);
            startStopRt.pivot = new Vector2(0.5f, 0.5f);
            startStopRt.anchoredPosition = new Vector2(195, 0);

            GameObject indicator = new GameObject("RecordingIndicator");
            indicator.transform.SetParent(buttonsPanel.transform, false);
            Image indicatorImg = indicator.AddComponent<Image>();
            indicatorImg.color = Color.red;
            indicatorImg.enabled = false;

            RectTransform indRt = indicator.GetComponent<RectTransform>();
            indRt.anchorMin = new Vector2(0.5f, 0.5f);
            indRt.anchorMax = new Vector2(0.5f, 0.5f);
            indRt.pivot = new Vector2(0.5f, 0.5f);
            indRt.anchoredPosition = new Vector2(395, 35);
            indRt.sizeDelta = new Vector2(36, 36);

            return panel;
        }

        private static GameObject CreateSettingsPanel(GameObject canvas)
        {
            GameObject panel = new GameObject("SettingsPanel");
            panel.transform.SetParent(canvas.transform, false);
            panel.SetActive(false);
            
            RectTransform rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            
            // ScrollView for settings
            GameObject scrollView = CreateScrollView(panel, "SettingsScrollView", new Vector2(0, 0.1f), new Vector2(1, 0.95f));
            GameObject content = scrollView.transform.Find("Viewport/Content").gameObject;
            
            // Title & Back Button (Fixed at top)
            GameObject titleGO = CreateTextTMP(panel, "Title", "Settings", Vector2.zero, new Vector2(400, 60), TextAlignmentOptions.Center, 36);
            RectTransform titleRt = titleGO.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0.5f, 1);
            titleRt.anchorMax = new Vector2(0.5f, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.anchoredPosition = new Vector2(0, -50);
            
            GameObject backBtn = CreateButton(panel, "BackButton", "< Back", new Vector2(50, -50), new Vector2(200, 60), new Color(0.4f, 0.4f, 0.4f), 34);
            
            float yPos = -20;
            
            // Server Settings Group
            yPos = CreateSectionHeader(content, "Server Settings", yPos);
            CreateSettingsLabel(content, "IPLabel", "IP Address", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject ipInput = CreateInputField(content, "IPAddressInput", "192.168.1.100", new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 80;
            CreateSettingsLabel(content, "PortLabel", "Port", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject portInput = CreateInputField(content, "PortInput", "50051", new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 100;
            
            // Camera Settings Group
            yPos = CreateSectionHeader(content, "Camera Settings", yPos);
            CreateSettingsLabel(content, "ResolutionLabel", "Resolution", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject resDropdown = CreateDropdown(content, "ResolutionDropdown", 
                new string[] { "1920x1080", "1280x720", "854x480", "640x480" }, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 80;
            CreateSettingsLabel(content, "FPSLabel", "FPS", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject fpsDropdown = CreateDropdown(content, "FPSDropdown", 
                new string[] { "60", "30", "20", "15" }, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 80;
            CreateSettingsLabel(content, "JPEGLabel", "JPEG Quality", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject jpegSlider = CreateSliderWithLabel(content, "JPEGQualitySlider", 50, 95, 75, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 100;
            
            // Sensor Settings Group
            yPos = CreateSectionHeader(content, "Sensor Settings", yPos);
            CreateSettingsLabel(content, "SamplingRateLabel", "Sampling Rate", new Vector2(50, yPos), new Vector2(220, 60), 28);
            GameObject samplingDropdown = CreateDropdown(content, "SamplingRateDropdown", 
                new string[] { "200Hz", "100Hz", "50Hz", "25Hz" }, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 80;
            GameObject accelToggle = CreateToggle(content, "EnableAccelToggle", "Accelerometer", true, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 70;
            GameObject gyroToggle = CreateToggle(content, "EnableGyroToggle", "Gyroscope", true, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 70;
            GameObject magToggle = CreateToggle(content, "EnableMagToggle", "Magnetometer", true, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 100;
            
            // Advanced Settings Group
            yPos = CreateSectionHeader(content, "Advanced Settings", yPos);
            GameObject autoQualityToggle = CreateToggle(content, "AutoAdjustQualityToggle", "Auto Adjust Quality", true, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 70;
            GameObject frameSkipToggle = CreateToggle(content, "EnableFrameSkipToggle", "Frame Skip", true, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 70;
            GameObject debugInfoToggle = CreateToggle(content, "ShowDebugInfoToggle", "Debug Info", false, new Vector2(0, yPos), new Vector2(700, 60));
            yPos -= 100;

            // Action Buttons (Fixed at bottom)
            GameObject bottomBar = new GameObject("SettingsBottomBar");
            bottomBar.transform.SetParent(panel.transform, false);
            RectTransform bottomBarRt = bottomBar.AddComponent<RectTransform>();
            bottomBarRt.anchorMin = new Vector2(0, 0);
            bottomBarRt.anchorMax = new Vector2(1, 0); // Anchor to bottom edge
            bottomBarRt.pivot = new Vector2(0.5f, 0);
            bottomBarRt.sizeDelta = new Vector2(0, 180); // Fixed height 180px
            bottomBarRt.anchoredPosition = Vector2.zero;
            
            Image bottomBarBg = bottomBar.AddComponent<Image>();
            bottomBarBg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);

            // Center Save/Reset buttons
            GameObject saveBtn = CreateButton(bottomBar, "SaveButton", "Save", Vector2.zero, new Vector2(450, 100), new Color(0.2f, 0.7f, 0.2f), 46);
            RectTransform saveRt = saveBtn.GetComponent<RectTransform>();
            saveRt.anchorMin = new Vector2(0.5f, 0.5f);
            saveRt.anchorMax = new Vector2(0.5f, 0.5f);
            saveRt.pivot = new Vector2(1, 0.5f); // Pivot Right
            saveRt.anchoredPosition = new Vector2(-20, 0); // 20px left of center

            GameObject resetBtn = CreateButton(bottomBar, "ResetButton", "Reset", Vector2.zero, new Vector2(450, 100), new Color(0.7f, 0.3f, 0.2f), 46);
            RectTransform resetRt = resetBtn.GetComponent<RectTransform>();
            resetRt.anchorMin = new Vector2(0.5f, 0.5f);
            resetRt.anchorMax = new Vector2(0.5f, 0.5f);
            resetRt.pivot = new Vector2(0, 0.5f); // Pivot Left
            resetRt.anchoredPosition = new Vector2(20, 0); // 20px right of center
            
            // Set Content height
            RectTransform contentRt = content.GetComponent<RectTransform>();
            // Add extra bottom padding for the fixed bottom bar
            contentRt.sizeDelta = new Vector2(1000, Mathf.Abs(yPos) + 300);
            
            // Add SettingsPanel component and wire up references
            SettingsPanel settingsPanel = panel.AddComponent<SettingsPanel>();
            SerializedObject spSo = new SerializedObject(settingsPanel);
            
            spSo.FindProperty("backButton").objectReferenceValue = backBtn.GetComponent<Button>();
            spSo.FindProperty("saveButton").objectReferenceValue = saveBtn.GetComponent<Button>();
            spSo.FindProperty("resetButton").objectReferenceValue = resetBtn.GetComponent<Button>();
            
            spSo.FindProperty("ipAddressInput").objectReferenceValue = ipInput.GetComponent<TMP_InputField>();
            spSo.FindProperty("portInput").objectReferenceValue = portInput.GetComponent<TMP_InputField>();
            
            spSo.FindProperty("resolutionDropdown").objectReferenceValue = resDropdown.GetComponent<TMP_Dropdown>();
            spSo.FindProperty("fpsDropdown").objectReferenceValue = fpsDropdown.GetComponent<TMP_Dropdown>();
            spSo.FindProperty("jpegQualitySlider").objectReferenceValue = jpegSlider.GetComponent<Slider>();
            spSo.FindProperty("jpegQualityText").objectReferenceValue = jpegSlider.transform.Find("ValueText").GetComponent<TextMeshProUGUI>();
            
            spSo.FindProperty("samplingRateDropdown").objectReferenceValue = samplingDropdown.GetComponent<TMP_Dropdown>();
            spSo.FindProperty("enableAccelToggle").objectReferenceValue = accelToggle.GetComponent<Toggle>();
            spSo.FindProperty("enableGyroToggle").objectReferenceValue = gyroToggle.GetComponent<Toggle>();
            spSo.FindProperty("enableMagToggle").objectReferenceValue = magToggle.GetComponent<Toggle>();
            
            spSo.FindProperty("autoAdjustQualityToggle").objectReferenceValue = autoQualityToggle.GetComponent<Toggle>();
            spSo.FindProperty("enableFrameSkipToggle").objectReferenceValue = frameSkipToggle.GetComponent<Toggle>();
            spSo.FindProperty("showDebugInfoToggle").objectReferenceValue = debugInfoToggle.GetComponent<Toggle>();
            
            spSo.ApplyModifiedProperties();
            
            return panel;
        }

        private static GameObject CreateButton(GameObject parent, string name, string text, Vector2 position, Vector2 size, Color color, float fontSize = 42)
        {
            GameObject btnGO = new GameObject(name);
            btnGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = btnGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            
            Image img = btnGO.AddComponent<Image>();
            img.color = color;
            
            Button btn = btnGO.AddComponent<Button>();
            
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);
            
            TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Normal;
            tmp.enableAutoSizing = false;
            
            RectTransform textRt = textGO.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            
            return btnGO;
        }

        private static GameObject CreateTextTMP(GameObject parent, string name, string text, Vector2 position, Vector2 size, TextAlignmentOptions alignment, float fontSize = 26)
        {
            GameObject textGO = new GameObject(name);
            textGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = textGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            
            TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = alignment;
            tmp.enableAutoSizing = false;
            tmp.fontStyle = FontStyles.Normal;
            
            return textGO;
        }

        private static GameObject CreateSettingsLabel(GameObject parent, string name, string text, Vector2 position, Vector2 size, float fontSize = 28)
        {
            GameObject textGO = new GameObject(name);
            textGO.transform.SetParent(parent.transform, false);

            RectTransform rt = textGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            TextMeshProUGUI tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableAutoSizing = false;
            tmp.fontStyle = FontStyles.Normal;

            return textGO;
        }

        private static GameObject CreateUIManager(
            IMUSensorManager sensorManager,
            RawImage cameraPreview,
            GameObject statusBar,
            GameObject sensorDisplay,
            Button startStopBtn,
            Button settingsBtn,
            GameObject settingsPanel)
        {
            GameObject go = new GameObject("UIManager");
            UIManager uiManager = go.AddComponent<UIManager>();
            
            // Use SerializedObject to set private fields
            SerializedObject so = new SerializedObject(uiManager);
            
            so.FindProperty("sensorManager").objectReferenceValue = sensorManager;
            // cameraCapture は UIManager.Start() で自動作成されるため設定不要
            so.FindProperty("cameraPreviewImage").objectReferenceValue = cameraPreview;
            so.FindProperty("connectionStatusText").objectReferenceValue = statusBar.transform.Find("InfoPanel/ConnectionStatus").GetComponent<TextMeshProUGUI>();
            so.FindProperty("fpsCounterText").objectReferenceValue = statusBar.transform.Find("InfoPanel/FPSCounter").GetComponent<TextMeshProUGUI>();
            so.FindProperty("queueSizeText").objectReferenceValue = statusBar.transform.Find("InfoPanel/QueueSize").GetComponent<TextMeshProUGUI>();
            so.FindProperty("accelText").objectReferenceValue = sensorDisplay.transform.Find("InfoPanel/AccelText").GetComponent<TextMeshProUGUI>();
            so.FindProperty("gyroText").objectReferenceValue = sensorDisplay.transform.Find("InfoPanel/GyroText").GetComponent<TextMeshProUGUI>();
            so.FindProperty("magText").objectReferenceValue = sensorDisplay.transform.Find("InfoPanel/MagText").GetComponent<TextMeshProUGUI>();
            so.FindProperty("settingsButton").objectReferenceValue = settingsBtn;
            so.FindProperty("startStopButton").objectReferenceValue = startStopBtn;
            so.FindProperty("startStopButtonText").objectReferenceValue = startStopBtn.transform.Find("Text").GetComponent<TextMeshProUGUI>();
            so.FindProperty("recordingIndicator").objectReferenceValue = statusBar.transform.Find("ButtonsPanel/RecordingIndicator").GetComponent<Image>();
            so.FindProperty("settingsPanel").objectReferenceValue = settingsPanel;
            
            so.ApplyModifiedProperties();
            
            return go;
        }
        
        // === Settings UI Helper Methods ===
        
        private static GameObject CreateScrollView(GameObject parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject scrollViewGO = new GameObject(name);
            scrollViewGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = scrollViewGO.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            
            ScrollRect scrollRect = scrollViewGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            
            // Viewport
            GameObject viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollViewGO.transform, false);
            RectTransform viewportRt = viewport.AddComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewport.AddComponent<RectMask2D>();
            
            // Content
            GameObject content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRt = content.AddComponent<RectTransform>();
            // Use centered column layout for better landscape support
            contentRt.anchorMin = new Vector2(0.5f, 1); 
            contentRt.anchorMax = new Vector2(0.5f, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.sizeDelta = new Vector2(1000, 2000);
            
            scrollRect.viewport = viewportRt;
            scrollRect.content = contentRt;
            
            return scrollViewGO;
        }
        
        private static float CreateSectionHeader(GameObject parent, string title, float yPos)
        {
            CreateTextTMP(parent, title + "Header", title, new Vector2(50, yPos), new Vector2(980, 50), TextAlignmentOptions.Left, 34);
            // Give a bit more breathing room between section header and first item label.
            return yPos - 90;
        }
        
        private static GameObject CreateInputField(GameObject parent, string name, string placeholder, Vector2 position, Vector2 size)
        {
            // InputField background
            GameObject inputGO = new GameObject(name);
            inputGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = inputGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(250, position.y);
            rt.sizeDelta = size;
            
            Image bg = inputGO.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f);
            
            TMP_InputField inputField = inputGO.AddComponent<TMP_InputField>();
            
            // Text Area
            GameObject textArea = new GameObject("TextArea");
            textArea.transform.SetParent(inputGO.transform, false);
            RectTransform textAreaRt = textArea.AddComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.offsetMin = new Vector2(10, 0);
            textAreaRt.offsetMax = new Vector2(-10, 0);
            textArea.AddComponent<RectMask2D>();
            
            // Placeholder
            GameObject placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            TextMeshProUGUI placeholderText = placeholderGO.AddComponent<TextMeshProUGUI>();
            placeholderText.text = placeholder;
            placeholderText.fontSize = 26;
            placeholderText.color = new Color(0.5f, 0.5f, 0.5f);
            placeholderText.alignment = TextAlignmentOptions.Left;
            RectTransform phRt = placeholderGO.GetComponent<RectTransform>();
            phRt.anchorMin = Vector2.zero;
            phRt.anchorMax = Vector2.one;
            phRt.offsetMin = Vector2.zero;
            phRt.offsetMax = Vector2.zero;
            
            // Text
            GameObject textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            TextMeshProUGUI text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = "";
            text.fontSize = 26;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
            RectTransform txtRt = textGO.GetComponent<RectTransform>();
            txtRt.anchorMin = Vector2.zero;
            txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = Vector2.zero;
            txtRt.offsetMax = Vector2.zero;
            
            inputField.textViewport = textAreaRt;
            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            
            return inputGO;
        }
        
        private static GameObject CreateDropdown(GameObject parent, string name, string[] options, Vector2 position, Vector2 size)
        {
            // Dropdown
            GameObject dropdownGO = new GameObject(name);
            dropdownGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = dropdownGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(250, position.y);
            rt.sizeDelta = size;
            
            Image bg = dropdownGO.AddComponent<Image>();
            bg.color = new Color(0.2f, 0.2f, 0.2f);
            
            TMP_Dropdown dropdown = dropdownGO.AddComponent<TMP_Dropdown>();
            
            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(dropdownGO.transform, false);
            TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = options[0];
            labelText.fontSize = 26;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Left;
            RectTransform labelRt = labelGO.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(15, 0);
            labelRt.offsetMax = new Vector2(-40, 0);
            
            // Arrow
            GameObject arrow = new GameObject("Arrow");
            arrow.transform.SetParent(dropdownGO.transform, false);
            TextMeshProUGUI arrowText = arrow.AddComponent<TextMeshProUGUI>();
            arrowText.text = "▼";
            arrowText.fontSize = 22;
            arrowText.color = Color.white;
            arrowText.alignment = TextAlignmentOptions.Center;
            RectTransform arrowRt = arrow.GetComponent<RectTransform>();
            arrowRt.anchorMin = new Vector2(1, 0);
            arrowRt.anchorMax = new Vector2(1, 1);
            arrowRt.sizeDelta = new Vector2(30, 0);
            arrowRt.anchoredPosition = new Vector2(-15, 0);
            
            // Template (simplified, not fully functional dropdown list)
            GameObject template = new GameObject("Template");
            template.transform.SetParent(dropdownGO.transform, false);
            template.SetActive(false);
            RectTransform templateRt = template.AddComponent<RectTransform>();
            templateRt.anchorMin = new Vector2(0, 0);
            templateRt.anchorMax = new Vector2(1, 0);
            templateRt.pivot = new Vector2(0.5f, 1);
            templateRt.anchoredPosition = new Vector2(0, 2);
            templateRt.sizeDelta = new Vector2(0, 150);
            
            dropdown.targetGraphic = bg;
            dropdown.captionText = labelText;
            dropdown.template = templateRt;
            
            dropdown.options.Clear();
            foreach (string opt in options)
            {
                dropdown.options.Add(new TMP_Dropdown.OptionData(opt));
            }
            dropdown.value = 0;
            dropdown.RefreshShownValue();
            
            return dropdownGO;
        }
        
        private static GameObject CreateSliderWithLabel(GameObject parent, string name, float min, float max, float value, Vector2 position, Vector2 size)
        {
            // Slider container
            GameObject sliderGO = new GameObject(name);
            sliderGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = sliderGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(250, position.y);
            rt.sizeDelta = size;
            
            Slider slider = sliderGO.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = value;
            slider.wholeNumbers = true;
            
            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            RectTransform bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.25f);
            bgRt.anchorMax = new Vector2(0.85f, 0.75f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            
            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderGO.transform, false);
            RectTransform fillAreaRt = fillArea.AddComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0, 0.25f);
            fillAreaRt.anchorMax = new Vector2(0.85f, 0.75f);
            fillAreaRt.offsetMin = new Vector2(5, 0);
            fillAreaRt.offsetMax = new Vector2(-5, 0);
            
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = new Color(0.2f, 0.7f, 0.2f);
            RectTransform fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            
            // Handle Slide Area
            GameObject handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderGO.transform, false);
            RectTransform handleAreaRt = handleArea.AddComponent<RectTransform>();
            handleAreaRt.anchorMin = new Vector2(0, 0);
            handleAreaRt.anchorMax = new Vector2(0.85f, 1);
            handleAreaRt.offsetMin = Vector2.zero;
            handleAreaRt.offsetMax = Vector2.zero;
            
            GameObject handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            Image handleImg = handle.AddComponent<Image>();
            handleImg.color = Color.white;
            RectTransform handleRt = handle.GetComponent<RectTransform>();
            handleRt.sizeDelta = new Vector2(20, 0);
            
            // Value Text
            GameObject valueText = new GameObject("ValueText");
            valueText.transform.SetParent(sliderGO.transform, false);
            TextMeshProUGUI valueTmp = valueText.AddComponent<TextMeshProUGUI>();
            valueTmp.text = value.ToString("F0");
            valueTmp.fontSize = 30;
            valueTmp.color = Color.white;
            valueTmp.alignment = TextAlignmentOptions.Center;
            RectTransform valueTmpRt = valueText.GetComponent<RectTransform>();
            valueTmpRt.anchorMin = new Vector2(0.87f, 0);
            valueTmpRt.anchorMax = new Vector2(1, 1);
            valueTmpRt.offsetMin = Vector2.zero;
            valueTmpRt.offsetMax = Vector2.zero;
            
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            
            return sliderGO;
        }
        
        private static GameObject CreateToggle(GameObject parent, string name, string label, bool isOn, Vector2 position, Vector2 size)
        {
            GameObject toggleGO = new GameObject(name);
            toggleGO.transform.SetParent(parent.transform, false);
            
            RectTransform rt = toggleGO.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(250, position.y);
            rt.sizeDelta = size;
            
            Toggle toggle = toggleGO.AddComponent<Toggle>();
            toggle.isOn = isOn;
            
            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(toggleGO.transform, false);
            Image bgImg = bg.AddComponent<Image>();
            bgImg.color = new Color(0.2f, 0.2f, 0.2f);
            RectTransform bgRt = bg.GetComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0, 0.5f);
            bgRt.anchorMax = new Vector2(0, 0.5f);
            bgRt.pivot = new Vector2(0, 0.5f);
            bgRt.anchoredPosition = new Vector2(30, 0);
            bgRt.sizeDelta = new Vector2(50, 50);
            
            // Checkmark
            GameObject checkmark = new GameObject("Checkmark");
            checkmark.transform.SetParent(bg.transform, false);
            Image checkImg = checkmark.AddComponent<Image>();
            checkImg.color = new Color(0.2f, 0.8f, 0.2f);
            RectTransform checkRt = checkmark.GetComponent<RectTransform>();
            checkRt.anchorMin = Vector2.zero;
            checkRt.anchorMax = Vector2.one;
            checkRt.offsetMin = new Vector2(5, 5);
            checkRt.offsetMax = new Vector2(-5, -5);
            
            // Label
            GameObject labelGO = new GameObject("Label");
            labelGO.transform.SetParent(toggleGO.transform, false);
            TextMeshProUGUI labelText = labelGO.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 28;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Left;
            RectTransform labelRt = labelGO.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 1);
            labelRt.offsetMin = new Vector2(90, 0);
            labelRt.offsetMax = Vector2.zero;
            
            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;
            
            return toggleGO;
        }
    }
}
