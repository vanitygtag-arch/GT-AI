using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

namespace GorillaTagAI
{
    /// <summary>
    /// World-space VR menu. This UI is presentation-only: it does not change player physics.
    /// </summary>
    public sealed class VRMenuController : MonoBehaviour
    {
        private enum Page { Dashboard, Configuration, Library, Stats }

        private static readonly Color BackgroundColor = new Color(0.012f, 0.025f, 0.045f);
        private static readonly Color PanelColor = new Color(0.025f, 0.065f, 0.095f);
        private static readonly Color ButtonColor = new Color(0.045f, 0.17f, 0.23f);
        private static readonly Color SelectedColor = new Color(0.02f, 0.42f, 0.52f);
        private static readonly Color HoverColor = new Color(0.04f, 0.58f, 0.68f);
        private static readonly Color AccentColor = new Color(0.15f, 0.82f, 0.92f);

        private Page page = Page.Dashboard;
        private GameObject root;
        private readonly List<GameObject> pageObjects = new List<GameObject>();
        private LineRenderer pointer;
        private VRMenuButton hoveredButton;
        private TextMesh footerStatusText;
        private float nextScrollTime;
        private string status = "Ready";
        private bool previousOpen;
        private bool previousBack;
        private bool previousSelect;

        private void Start()
        {
            Debug.Log("[GorillaTagAI] VRMenuController started.");
            InspectControllerInput();
            BuildMenu();
            SetVisible(false);
        }

        private void Update()
        {
            if (root == null) return;

            if (Time.frameCount % 600 == 0)
                Debug.Log("[GorillaTagAI] VR menu input polling is active.");

            ControllerInputPoller poller = ControllerInputPoller.instance;
            if (poller == null)
            {
                if (Time.frameCount % 300 == 0)
                    Debug.LogWarning("[GorillaTagAI] ControllerInputPoller is not ready.");
                return;
            }

            // Use Gorilla Tag's own left controller device for the menu toggle.
            InputDevice left = poller.leftControllerDevice;
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            bool open = false;
            if (left.isValid)
                left.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out open);

            bool back = ReadButton(left, CommonUsages.gripButton);
            bool select = ReadButton(right, CommonUsages.triggerButton);

            if (open && !previousOpen)
            {
                bool show = !root.activeSelf;
                if (show) PlaceInFrontOfHead();
                SetVisible(show);
            }

            if (root.activeSelf)
            {
                if (back && !previousBack) GoBack();
                UpdatePointer(right);
                if (select && !previousSelect) SelectPointerTarget();
                UpdateScroll(right);
                FaceMenuToHead();
            }

            previousOpen = open;
            previousBack = back;
            previousSelect = select;
        }

        private static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage)
        {
            bool value;
            return device.isValid && device.TryGetFeatureValue(usage, out value) && value;
        }

        private void BuildMenu()
        {
            root = new GameObject("GorillaTagAI_VRMenu");
            root.transform.SetParent(transform, false);

            GameObject backplate = CreateBlock(
                "MenuBackplate",
                new Vector3(0f, 0f, -0.055f),
                new Vector3(1.08f, 1.00f, 0.045f),
                BackgroundColor,
                false);
            backplate.transform.SetParent(root.transform, false);

            GameObject accent = CreateBlock(
                "MenuAccent",
                new Vector3(-0.515f, 0f, -0.025f),
                new Vector3(0.012f, 0.94f, 0.018f),
                AccentColor,
                false);
            accent.transform.SetParent(root.transform, false);

            GameObject pointerObject = new GameObject("RightControllerPointer");
            pointerObject.transform.SetParent(root.transform, false);
            pointer = pointerObject.AddComponent<LineRenderer>();
            pointer.useWorldSpace = true;
            pointer.positionCount = 2;
            pointer.startWidth = 0.006f;
            pointer.endWidth = 0.002f;
            pointer.material = new Material(Shader.Find("Sprites/Default"));
            pointer.startColor = AccentColor;
            pointer.endColor = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.15f);
            pointer.enabled = false;

            ShowPage(Page.Dashboard);
        }

        private GameObject CreateBlock(string objectName, Vector3 localPosition, Vector3 localScale, Color color, bool colliderEnabled)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = objectName;
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;

            Renderer renderer = block.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;

            Collider blockCollider = block.GetComponent<Collider>();
            if (blockCollider != null) blockCollider.enabled = colliderEnabled;
            return block;
        }

        private void SetVisible(bool visible)
        {
            if (root == null) return;
            root.SetActive(visible);
            if (!visible && hoveredButton != null)
            {
                hoveredButton.SetHighlighted(false);
                hoveredButton = null;
            }
            Debug.Log("[GorillaTagAI] VR menu visible: " + visible);
        }

        private void PlaceInFrontOfHead()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[GorillaTagAI] Main VR camera not found; cannot place menu.");
                return;
            }

            Transform head = cam.transform;
            Vector3 position = head.position + head.forward * 1.15f - Vector3.up * 0.05f;
            Vector3 towardHead = head.position - position;
            towardHead = Vector3.ProjectOnPlane(towardHead, Vector3.up).normalized;
            if (towardHead.sqrMagnitude < 0.001f)
                towardHead = -head.forward;

            // The menu's local +Z face points toward the player.
            root.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(towardHead, Vector3.up));

            Debug.Log("[GorillaTagAI] VR menu placed in front of the headset camera.");
        }

        private void FaceMenuToHead()
        {
            if (root == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 towardHead = cam.transform.position - root.transform.position;
            towardHead = Vector3.ProjectOnPlane(towardHead, Vector3.up);
            if (towardHead.sqrMagnitude > 0.001f)
                root.transform.rotation = Quaternion.LookRotation(towardHead.normalized, Vector3.up);
        }

        private void UpdatePointer(InputDevice right)
        {
            Vector3 origin;
            Quaternion rotation;
            if (!right.isValid ||
                !right.TryGetFeatureValue(CommonUsages.devicePosition, out origin) ||
                !right.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
            {
                pointer.enabled = false;
                SetHoveredButton(null);
                return;
            }

            Vector3 direction = rotation * Vector3.forward;
            pointer.enabled = true;
            pointer.SetPosition(0, origin);
            pointer.SetPosition(1, origin + direction * 5f);

            RaycastHit hit;
            VRMenuButton next = null;
            if (Physics.Raycast(origin, direction, out hit, 5f))
                next = hit.collider.GetComponent<VRMenuButton>();

            SetHoveredButton(next);
        }

        private void SetHoveredButton(VRMenuButton next)
        {
            if (hoveredButton == next) return;
            if (hoveredButton != null) hoveredButton.SetHighlighted(false);
            hoveredButton = next;
            if (hoveredButton != null) hoveredButton.SetHighlighted(true);
        }

        private void SelectPointerTarget()
        {
            if (hoveredButton != null)
                hoveredButton.Invoke();
        }

        private void UpdateScroll(InputDevice right)
        {
            Vector2 axis;
            if (Time.unscaledTime < nextScrollTime || !right.isValid ||
                !right.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis))
                return;

            if (Mathf.Abs(axis.y) > 0.75f)
            {
                nextScrollTime = Time.unscaledTime + 0.25f;
                SetStatus(axis.y > 0f ? "Library: previous item" : "Library: next item");
            }
        }

        private void ClearPage()
        {
            SetHoveredButton(null);
            footerStatusText = null;

            for (int i = 0; i < pageObjects.Count; i++)
                if (pageObjects[i] != null) Destroy(pageObjects[i]);

            pageObjects.Clear();
        }

        private void ShowPage(Page next)
        {
            page = next;
            ClearPage();

            AddPanel("GORILLA TAG AI", 0f, 0.405f, 1.00f, 0.13f);
            AddButton("HOME", -0.36f, 0.255f, () => ShowPage(Page.Dashboard), 0.21f, 0.085f, page == Page.Dashboard);
            AddButton("SETUP", -0.12f, 0.255f, () => ShowPage(Page.Configuration), 0.21f, 0.085f, page == Page.Configuration);
            AddButton("LIBRARY", 0.12f, 0.255f, () => ShowPage(Page.Library), 0.21f, 0.085f, page == Page.Library);
            AddButton("STATS", 0.36f, 0.255f, () => ShowPage(Page.Stats), 0.21f, 0.085f, page == Page.Stats);

            AddPanel(null, 0f, -0.075f, 0.98f, 0.56f);

            if (page == Page.Dashboard)
            {
                AddText("CONTROL CENTER", 0f, 0.125f, true, 0.017f);
                AddText("AI ENGINE", -0.27f, 0.005f, true);
                AddText(AIStateText(), -0.27f, -0.055f);
                AddButton("TOGGLE AI", 0.27f, -0.025f,
                    () => { Plugin.AIEnabled.Value = !Plugin.AIEnabled.Value; ShowPage(Page.Dashboard); },
                    0.30f, 0.09f);

                AddText("LEARNING", -0.27f, -0.155f, true);
                AddText(LearningStateText(), -0.27f, -0.215f);
                AddButton("TOGGLE LEARNING", 0.27f, -0.18f,
                    () => { Plugin.LearningEnabled.Value = !Plugin.LearningEnabled.Value; ShowPage(Page.Dashboard); },
                    0.34f, 0.09f);

                AddText("Learning only records while both switches are enabled.", 0f, -0.305f, false, 0.011f);
            }
            else if (page == Page.Configuration)
            {
                AddText("CONFIGURATION", 0f, 0.135f, true, 0.017f);
                AddText("Target label: " + Plugin.LearningTarget.Value, 0f, 0.045f);
                AddText("Objective: " + Plugin.ObjectivePreset.Value, 0f, -0.045f);

                AddButton("OBSERVE ONLY", -0.245f, -0.16f, () => SetObjective("ObserveOnly"), 0.40f, 0.075f);
                AddButton("LEARN MOVEMENT", 0.245f, -0.16f, () => SetObjective("LearnMovement"), 0.40f, 0.075f);
                AddButton("PRACTICE CLIMBING", -0.245f, -0.255f, () => SetObjective("PracticeClimbing"), 0.40f, 0.075f);
                AddButton("SLIDING", 0.245f, -0.255f, () => SetObjective("Sliding"), 0.40f, 0.075f);
                AddButton("INFECTION CHASE", -0.245f, -0.35f, () => SetObjective("InfectionChase"), 0.40f, 0.075f);
                AddButton("INFECTION ESCAPE", 0.245f, -0.35f, () => SetObjective("InfectionEscape"), 0.40f, 0.075f);
            }
            else if (page == Page.Library)
            {
                AddText("MOVEMENT LIBRARY", 0f, 0.135f, true, 0.017f);
                AddText("Saved demonstrations will appear here.", 0f, 0.025f);
                AddText("The movement recorder is not connected yet.", 0f, -0.055f);
                AddText("No replay or hologram data is being generated.", 0f, -0.135f);
                AddButton("CAPTURE DEMO (PENDING)", 0f, -0.265f,
                    () => SetStatus("Capture is unavailable until the recorder is integrated."),
                    0.52f, 0.09f);
            }
            else
            {
                AddText("LEARNING STATISTICS", 0f, 0.135f, true, 0.017f);
                AddText("Statistics require verified movement samples.", 0f, 0.035f);
                AddText("The observation adapter is not connected yet.", 0f, -0.045f);
                AddText("Learning storage remains local to this device.", 0f, -0.125f);
                AddText(status, 0f, -0.235f);
            }

            AddText("L3  OPEN/CLOSE     GRIP  BACK     TRIGGER  SELECT", 0f, -0.445f, false, 0.0105f);
            footerStatusText = AddText(status, 0f, -0.475f, false, 0.0095f);
        }

        private string AIStateText()
        {
            return Plugin.AIEnabled.Value ? "Enabled" : "Disabled";
        }

        private string LearningStateText()
        {
            return Plugin.LearningEnabled.Value ? "Enabled" : "Disabled";
        }

        private void SetStatus(string value)
        {
            status = value;
            if (footerStatusText != null) footerStatusText.text = value;
        }

        private void SetObjective(string value)
        {
            Plugin.ObjectivePreset.Value = value;
            ShowPage(Page.Configuration);
        }

        private void GoBack()
        {
            if (page != Page.Dashboard)
                ShowPage(Page.Dashboard);
            else
                SetVisible(false);
        }

        private void AddPanel(string label, float x, float y, float width, float height)
        {
            GameObject panel = CreateBlock(
                "MenuPanel",
                new Vector3(x, y, 0f),
                new Vector3(width, height, 0.035f),
                PanelColor,
                false);
            panel.transform.SetParent(root.transform, false);
            pageObjects.Add(panel);

            if (!string.IsNullOrEmpty(label))
                AddText(label, x, y, true, 0.019f);
        }

        private void AddButton(
            string label,
            float x,
            float y,
            Action action,
            float width = 0.23f,
            float height = 0.085f,
            bool selected = false)
        {
            GameObject button = CreateBlock(
                "MenuButton_" + label,
                new Vector3(x, y, 0.032f),
                new Vector3(width, height, 0.045f),
                selected ? SelectedColor : ButtonColor,
                true);
            button.transform.SetParent(root.transform, false);

            Renderer renderer = button.GetComponent<Renderer>();
            VRMenuButton handler = button.AddComponent<VRMenuButton>();
            handler.SetAction(action);
            handler.SetVisual(renderer, selected ? SelectedColor : ButtonColor, HoverColor);

            pageObjects.Add(button);
            AddText(label, x, y, true, 0.0125f);
        }

        private TextMesh AddText(string value, float x, float y, bool emphasized = false, float characterSize = 0.014f)
        {
            GameObject textObject = new GameObject("MenuText");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(x, y, 0.061f);

            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = emphasized ? 52 : 40;
            text.characterSize = characterSize;
            text.fontStyle = emphasized ? FontStyle.Bold : FontStyle.Normal;
            text.color = Color.white;
            text.richText = false;

            pageObjects.Add(textObject);
            return text;
        }

        private void InspectControllerInput()
        {
            Type type = typeof(ControllerInputPoller);

            foreach (FieldInfo field in type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = field.Name.ToLowerInvariant();
                if (name.Contains("left") || name.Contains("right") ||
                    name.Contains("trigger") || name.Contains("grip") ||
                    name.Contains("joystick") || name.Contains("button") ||
                    name.Contains("axis"))
                {
                    Debug.Log("[GorillaTagAI] INPUT FIELD: " +
                        field.Name + " (" + field.FieldType.Name + ")");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                string name = property.Name.ToLowerInvariant();
                if (name.Contains("left") || name.Contains("right") ||
                    name.Contains("trigger") || name.Contains("grip") ||
                    name.Contains("joystick") || name.Contains("button") ||
                    name.Contains("axis"))
                {
                    Debug.Log("[GorillaTagAI] INPUT PROPERTY: " +
                        property.Name + " (" + property.PropertyType.Name + ")");
                }
            }
        }
    }

    public sealed class VRMenuButton : MonoBehaviour
    {
        private Action action;
        private Renderer buttonRenderer;
        private Color normalColor;
        private Color highlightedColor;

        public void SetAction(Action callback)
        {
            action = callback;
        }

        public void SetVisual(Renderer renderer, Color normal, Color highlighted)
        {
            buttonRenderer = renderer;
            normalColor = normal;
            highlightedColor = highlighted;
        }

        public void SetHighlighted(bool highlighted)
        {
            if (buttonRenderer != null)
                buttonRenderer.material.color = highlighted ? highlightedColor : normalColor;
        }

        public void Invoke()
        {
            if (action != null) action.Invoke();
        }
    }
}