using UnityEngine;

namespace StargazerProbe.UI
{
    public class ResponsiveUILayout : MonoBehaviour
    {
        // Serialized Fields - References
        [Header("References")]
        [SerializeField] private RectTransform cameraPreviewPanel;
        [SerializeField] private RectTransform controlPanel;
        [SerializeField] private RectTransform buttonsPanel;
        [SerializeField] private RectTransform infoPanel;

        // Serialized Fields - Info Text References
        [Header("Info Text")]
        [SerializeField] private RectTransform connectionStatus;
        [SerializeField] private RectTransform fpsCounter;
        [SerializeField] private RectTransform queueSize;
        [SerializeField] private RectTransform accelText;
        [SerializeField] private RectTransform gyroText;
        [SerializeField] private RectTransform magText;

        // Serialized Fields - Sizing
        [Header("Sizing")]
        [SerializeField] private float portraitControlHeight = 360f;
        [SerializeField] private float landscapeControlHeight = 220f;
        [SerializeField] private float portraitInfoHeight = 190f;
        [SerializeField] private float portraitButtonsHeight = 150f;
        [SerializeField] private float portraitGap = 20f;
        [SerializeField] private float landscapeButtonsWidth = 720f;
        [SerializeField] private float landscapeInfoYOffset = 12f;

        // Private Fields - Screen Tracking
        private int lastWidth;
        private int lastHeight;

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            if (Screen.width == lastWidth && Screen.height == lastHeight)
            {
                return;
            }

            Apply();
        }

        private void Apply()
        {
            lastWidth = Screen.width;
            lastHeight = Screen.height;

            bool isLandscape = Screen.width > Screen.height;
            float controlHeight = isLandscape
                ? landscapeControlHeight
                : Mathf.Max(portraitControlHeight, portraitInfoHeight + portraitButtonsHeight + portraitGap);

            if (controlPanel != null)
            {
                controlPanel.sizeDelta = new Vector2(controlPanel.sizeDelta.x, controlHeight);
            }

            if (cameraPreviewPanel != null)
            {
                Vector2 offsetMin = cameraPreviewPanel.offsetMin;
                offsetMin.y = controlHeight;
                cameraPreviewPanel.offsetMin = offsetMin;
            }

            if (buttonsPanel == null || infoPanel == null)
            {
                return;
            }

            if (isLandscape)
            {
                ConfigureAnchors(buttonsPanel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f));
                buttonsPanel.anchoredPosition = Vector2.zero;
                buttonsPanel.offsetMin = Vector2.zero;
                buttonsPanel.offsetMax = Vector2.zero;
                buttonsPanel.sizeDelta = new Vector2(landscapeButtonsWidth, 0);

                ConfigureAnchors(infoPanel, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f));
                infoPanel.anchoredPosition = Vector2.zero;
                infoPanel.offsetMin = new Vector2(landscapeButtonsWidth, 0);
                infoPanel.offsetMax = Vector2.zero;
            }
            else
            {
                ConfigureAnchors(infoPanel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
                infoPanel.anchoredPosition = Vector2.zero;
                infoPanel.offsetMin = Vector2.zero;
                infoPanel.offsetMax = Vector2.zero;
                infoPanel.sizeDelta = new Vector2(0, portraitInfoHeight);

                ConfigureAnchors(buttonsPanel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0));
                buttonsPanel.anchoredPosition = Vector2.zero;
                buttonsPanel.offsetMin = Vector2.zero;
                buttonsPanel.offsetMax = Vector2.zero;
                buttonsPanel.sizeDelta = new Vector2(0, portraitButtonsHeight);
            }

            ApplyInfoTextLayout(isLandscape);
        }

        private void ApplyInfoTextLayout(bool isLandscape)
        {
            if (connectionStatus == null || fpsCounter == null || queueSize == null || accelText == null || gyroText == null || magText == null)
            {
                return;
            }

            if (isLandscape)
            {
                ConfigureAnchors(connectionStatus, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                ConfigureAnchors(fpsCounter, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                ConfigureAnchors(queueSize, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                ConfigureAnchors(accelText, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                ConfigureAnchors(gyroText, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));
                ConfigureAnchors(magText, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f));

                // Center the whole info block vertically to align with centered buttons.
                float y0 = landscapeInfoYOffset;
                connectionStatus.anchoredPosition = new Vector2(30, 52 + y0);
                fpsCounter.anchoredPosition = new Vector2(360, 52 + y0);
                queueSize.anchoredPosition = new Vector2(600, 52 + y0);

                accelText.anchoredPosition = new Vector2(30, 16 + y0);
                gyroText.anchoredPosition = new Vector2(30, -20 + y0);
                magText.anchoredPosition = new Vector2(30, -56 + y0);
            }
            else
            {
                ConfigureAnchors(connectionStatus, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
                ConfigureAnchors(fpsCounter, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
                ConfigureAnchors(queueSize, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
                ConfigureAnchors(accelText, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
                ConfigureAnchors(gyroText, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
                ConfigureAnchors(magText, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));

                connectionStatus.anchoredPosition = new Vector2(30, -10);
                fpsCounter.anchoredPosition = new Vector2(360, -10);
                queueSize.anchoredPosition = new Vector2(600, -10);

                float startY = -60;
                float stepY = 40;
                accelText.anchoredPosition = new Vector2(30, startY);
                gyroText.anchoredPosition = new Vector2(30, startY - stepY);
                magText.anchoredPosition = new Vector2(30, startY - stepY * 2);
            }
        }

        private static void ConfigureAnchors(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
        }
    }
}
