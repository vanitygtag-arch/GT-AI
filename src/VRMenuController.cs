using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace GorillaTagAI
{
    // Prototype VR menu. Uses Unity XR device features rather than changing player physics.
    public sealed class VRMenuController : MonoBehaviour
    {
        private enum Page { Dashboard, Configuration, Library, Stats }
        private Page page = Page.Dashboard;
        private GameObject root;
        private readonly List<GameObject> pageObjects = new List<GameObject>();
        private LineRenderer pointer;
        private float nextScrollTime;
        private string status = "Ready";
        private bool previousOpen;
        private bool previousBack;
        private bool previousSelect;

        private void Start()
        {
            BuildMenu();
            SetVisible(false);
        }

        private void Update()
        {
            if (Time.frameCount % 300 == 0)
{
    Debug.Log("[GorillaTagAI] VRMenuController Update is running.");
}

            ControllerInputPoller poller = ControllerInputPoller.instance;

InputDevice left = poller != null
    ? poller.leftControllerDevice
    : default(InputDevice);
            InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            bool open = false;

if (ControllerInputPoller.instance != null)
{
    ControllerInputPoller.instance.leftControllerDevice
        .TryGetFeatureValue(
            CommonUsages.primary2DAxisClick,
            out open
        );
}
            bool back = ReadButton(left, CommonUsages.gripButton);
            bool select = ReadButton(right, CommonUsages.triggerButton);

            if (open && !previousOpen)
            {
                if (root != null && !root.activeSelf) PlaceInFrontOfHead(head);
                SetVisible(root != null && !root.activeSelf);
            }
            if (root != null && root.activeSelf)
            {
                if (back && !previousBack) GoBack();
                UpdatePointer(right);
                if (select && !previousSelect) SelectPointerTarget();
                UpdateScroll(right);
                FaceMenuToHead(head);
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
            pointer = new GameObject("RightControllerPointer").AddComponent<LineRenderer>();
            pointer.transform.SetParent(root.transform, false);
            pointer.positionCount = 2;
            pointer.startWidth = 0.006f;
            pointer.endWidth = 0.002f;
            pointer.material = new Material(Shader.Find("Sprites/Default"));
            pointer.startColor = new Color(0.2f, 0.8f, 1f, 0.9f);
            pointer.endColor = new Color(0.2f, 0.8f, 1f, 0.2f);
            ShowPage(Page.Dashboard);
        }

        private void SetVisible(bool visible)
        {
            if (root != null) root.SetActive(visible);
        }
        
        private void PlaceInFrontOfHead(InputDevice head)
        {
            Camera cam = Camera.main;
            Debug.Log("[GorillaTagAI] Main camera: " +
    (cam == null ? "NULL" : cam.name));

Debug.Log("[GorillaTagAI] Menu position before placement: " +
    root.transform.position);

            if (cam == null)
            {
               Debug.LogError("[GorillaTagAI] Main VR camera not found!");
               return;
          }

           Transform headTransform = cam.transform;

           Vector3 forward = Vector3.ProjectOnPlane(
                headTransform.forward,
                Vector3.up
         ).normalized;

          if (forward.sqrMagnitude < 0.001f)
             forward = headTransform.forward;

         Vector3 position =
            headTransform.position +
             headTransform.forward * 1.35f -
             Vector3.up * 0.05f;

         Quaternion rotation =
              Quaternion.LookRotation(forward, Vector3.up);

         Debug.Log("[GorillaTagAI] New menu position: " + position);
         root.transform.SetPositionAndRotation(position, rotation);

            Debug.Log("[GorillaTagAI] VR menu positioned in front of camera.");
        }

        private void FaceMenuToHead(InputDevice head)
        {
            Vector3 headPosition; Quaternion headRotation;
            if (head.isValid && head.TryGetFeatureValue(CommonUsages.devicePosition, out headPosition) &&
                head.TryGetFeatureValue(CommonUsages.deviceRotation, out headRotation))
            {
                Vector3 toward = root.transform.position - headPosition;
                if (toward.sqrMagnitude > 0.001f)
                    root.transform.rotation = Quaternion.LookRotation(toward, Vector3.up);
            }
        }

        private void UpdatePointer(InputDevice right)
        {
            Vector3 origin; Quaternion rotation;
            if (!right.isValid || !right.TryGetFeatureValue(CommonUsages.devicePosition, out origin) ||
                !right.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation))
            {
                pointer.enabled = false;
                return;
            }
            pointer.enabled = true;
            pointer.SetPosition(0, origin);
            pointer.SetPosition(1, origin + rotation * Vector3.forward * 5f);
        }

        private void SelectPointerTarget()
        {
            if (pointer == null || !pointer.enabled) return;
            Vector3 origin = pointer.GetPosition(0);
            Vector3 direction = (pointer.GetPosition(1) - origin).normalized;
            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, 5f))
            {
                VRMenuButton button = hit.collider.GetComponent<VRMenuButton>();
                if (button != null) button.Invoke();
            }
        }

        private void UpdateScroll(InputDevice right)
        {
            Vector2 axis;
            if (Time.unscaledTime < nextScrollTime || !right.isValid ||
                !right.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis)) return;
            if (Mathf.Abs(axis.y) > 0.75f)
            {
                nextScrollTime = Time.unscaledTime + 0.25f;
                status = axis.y > 0 ? "Library: previous item" : "Library: next item";
            }
        }

        private void ClearPage()
        {
            for (int i = 0; i < pageObjects.Count; i++)
                if (pageObjects[i] != null) Destroy(pageObjects[i]);
            pageObjects.Clear();
        }

        private void ShowPage(Page next)
        {
            page = next;
            ClearPage();
            AddPanel("GORILLA TAG AI", 0f, 0.72f, 0.78f, 0.12f, null);
            AddButton("Dashboard", -0.28f, 0.53f, () => ShowPage(Page.Dashboard));
            AddButton("Configuration", 0f, 0.53f, () => ShowPage(Page.Configuration));
            AddButton("Movement Library", 0.28f, 0.53f, () => ShowPage(Page.Library));
            AddButton("Learning Stats", -0.28f, 0.36f, () => ShowPage(Page.Stats));

            if (page == Page.Dashboard)
            {
                AddText("AI: " + Plugin.AIEnabled.Value, -0.25f, 0.12f);
                AddButton("Toggle AI", 0.20f, 0.12f, () => { Plugin.AIEnabled.Value = !Plugin.AIEnabled.Value; ShowPage(Page.Dashboard); });
                AddText("Learning: " + Plugin.LearningEnabled.Value, -0.25f, -0.06f);
                AddButton("Toggle Learning", 0.20f, -0.06f, () => { Plugin.LearningEnabled.Value = !Plugin.LearningEnabled.Value; ShowPage(Page.Dashboard); });
                AddText("Menu controls: L3 open | Left Grip back", 0f, -0.30f);
            }
            else if (page == Page.Configuration)
            {
                AddText("Target: " + Plugin.LearningTarget.Value, 0f, 0.18f);
                AddText("Objective: " + Plugin.ObjectivePreset.Value, 0f, 0.02f);
                AddButton("Observe Only", 0f, -0.16f, () => SetObjective("ObserveOnly"));
                AddText("Edit target name in BepInEx config for now.", 0f, -0.34f);
            }
            else if (page == Page.Library)
            {
                AddText("MOVEMENT LIBRARY", 0f, 0.22f);
                AddText("No replay recordings are connected yet.", 0f, 0.04f);
                AddText("The replay viewer will display real saved motion", 0f, -0.10f);
                AddText("after the movement recorder is integrated.", 0f, -0.22f);
                AddButton("Capture (recorder pending)", 0f, -0.39f, () => { status = "Recorder adapter is not connected yet."; });
            }
            else
            {
                AddText("LEARNING STATISTICS", 0f, 0.18f);
                AddText("Statistics become available when the", 0f, 0.00f);
                AddText("verified movement recorder supplies samples.", 0f, -0.14f);
                AddText(status, 0f, -0.32f);
            }
        }

        private void SetObjective(string value)
        {
            Plugin.ObjectivePreset.Value = value;
            ShowPage(Page.Configuration);
        }

        private void GoBack()
        {
            if (page != Page.Dashboard) ShowPage(Page.Dashboard);
            else SetVisible(false);
        }

        private void AddPanel(string label, float x, float y, float width, float height, System.Action action)
        {
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "MenuPanel";
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(x, y, 0.04f);
            panel.transform.localScale = new Vector3(width, height, 0.025f);
            Renderer renderer = panel.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.025f, 0.12f, 0.19f, 0.92f);
            pageObjects.Add(panel);
            if (!string.IsNullOrEmpty(label)) AddText(label, x, y);
        }

        private void AddButton(string label, float x, float y, System.Action action)
        {
            GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
            button.name = "MenuButton_" + label;
            button.transform.SetParent(root.transform, false);
            button.transform.localPosition = new Vector3(x, y, 0f);
            button.transform.localScale = new Vector3(0.24f, 0.105f, 0.045f);
            Renderer renderer = button.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = new Color(0.05f, 0.32f, 0.48f);
            VRMenuButton handler = button.AddComponent<VRMenuButton>();
            handler.SetAction(action);
            pageObjects.Add(button);
            AddText(label, x, y);
        }

        private void AddText(string value, float x, float y)
        {
            GameObject textObject = new GameObject("MenuText");
            textObject.transform.SetParent(root.transform, false);
            textObject.transform.localPosition = new Vector3(x, y, -0.03f);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.008f;
            text.color = Color.white;
            pageObjects.Add(textObject);
        }

        private void InspectControllerInput()
{
    Type type = typeof(ControllerInputPoller);

    foreach (FieldInfo field in type.GetFields(
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic))
    {
        string name = field.Name.ToLowerInvariant();

        if (name.Contains("left") ||
            name.Contains("right") ||
            name.Contains("trigger") ||
            name.Contains("grip") ||
            name.Contains("joystick") ||
            name.Contains("button") ||
            name.Contains("axis"))
        {
            Debug.Log("[GorillaTagAI] INPUT FIELD: " +
                field.Name + " (" + field.FieldType.Name + ")");
        }
    }

    foreach (PropertyInfo property in type.GetProperties(
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic))
    {
        string name = property.Name.ToLowerInvariant();

        if (name.Contains("left") ||
            name.Contains("right") ||
            name.Contains("trigger") ||
            name.Contains("grip") ||
            name.Contains("joystick") ||
            name.Contains("button") ||
            name.Contains("axis"))
        {
            Debug.Log("[GorillaTagAI] INPUT PROPERTY: " +
                property.Name + " (" + property.PropertyType.Name + ")");
        }
    }
    
    }

    public sealed class VRMenuButton : MonoBehaviour
    {
        private System.Action action;
        public void SetAction(System.Action callback) { action = callback; }
        public void Invoke() { if (action != null) action(); }
    }
}
