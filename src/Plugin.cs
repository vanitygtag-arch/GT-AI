using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System.IO;

namespace GorillaTagAI
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "org.gorillatagai.core";
        public const string PluginName = "Gorilla Tag AI";
        public const string PluginVersion = "0.3.0";

        internal static ConfigEntry<bool> AIEnabled;
        internal static ConfigEntry<bool> LearningEnabled;
        internal static ConfigEntry<string> LearningTarget;
        internal static ConfigEntry<string> ObjectivePreset;

        internal static Plugin Instance { get; private set; }
        private LearningModule _learning;
        private bool _showWindow = true;
        private bool _showObjectiveChoices;
        private Rect _windowRect = new Rect(30f, 30f, 430f, 560f);
        private Vector2 _summaryScroll;

        private static readonly string[] ObjectiveOptions =
        {
            "ObserveOnly",
            "LearnMovement",
            "PracticeClimbing",
            "Sliding",
            "InfectionChase",
            "InfectionEscape"
        };

        private void Awake()
        {
            Instance = this;
            AIEnabled = Config.Bind("General", "AIEnabled", false, "Master switch. Starts disabled.");
            LearningEnabled = Config.Bind("Learning", "LearningEnabled", false, "Learning updates only while AI and Learning are enabled.");
            LearningTarget = Config.Bind("Learning", "LearningTarget", "None", "Target label. Player enumeration is not connected yet.");
            ObjectivePreset = Config.Bind("Objectives", "ObjectivePreset", "ObserveOnly", "Objective selection; only passive observation infrastructure exists.");

            try
{
    gameObject.AddComponent<VRMenuController>();
    Logger.LogInfo("VRMenuController component added successfully!");
}
catch (System.Exception ex)
{
    Logger.LogError("VRMenuController initialization failed: " + ex);
}
            _learning = new LearningModule(Paths.ConfigPath);
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
            Logger.LogInfo("Passive learning storage initialized. No game observations are connected yet.");
            Logger.LogInfo($"AI enabled: {AIEnabled.Value}; learning enabled: {LearningEnabled.Value}");
            Logger.LogInfo("Learning data file: " + _learning.DataFilePath);
        }

        private void Update()
        {
            if (false) _showWindow = !_showWindow;
            if (_learning != null && (!_learning.IsLearningAllowed(AIEnabled.Value, LearningEnabled.Value)))
                _learning.PauseStatus();
        }

        // Future verified game-observation adapters can submit anonymized movement features here.
        // This method deliberately does not discover players or read private player identifiers.
        internal static bool SubmitObservation(string sourceKind, string movementLabel, float speed, float verticalSpeed, bool grounded)
        {
            if (Instance == null || Instance._learning == null) return false;
            return Instance._learning.RecordObservation(
                AIEnabled.Value, LearningEnabled.Value, sourceKind, movementLabel,
                speed, verticalSpeed, grounded);
        }

        private void OnGUI()
        {
            if (_showWindow) _windowRect = GUI.Window(817204, _windowRect, DrawWindow, "Gorilla Tag AI");
            else if (GUI.Button(new Rect(12f, 12f, 170f, 30f), "Open Gorilla Tag AI (F8)")) _showWindow = true;
        }

        private void DrawWindow(int windowId)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("CONTROL PANEL");
            bool ai = GUILayout.Toggle(AIEnabled.Value, " AI Enabled");
            if (ai != AIEnabled.Value)
            {
                AIEnabled.Value = ai;
                Logger.LogInfo("AI enabled changed to: " + ai);
            }
            bool learning = GUILayout.Toggle(LearningEnabled.Value, " Learning Enabled");
            if (learning != LearningEnabled.Value)
            {
                LearningEnabled.Value = learning;
                Logger.LogInfo("Learning enabled changed to: " + learning);
            }

            bool active = _learning != null && _learning.IsLearningAllowed(AIEnabled.Value, LearningEnabled.Value);
            GUILayout.Label(active ? "Learning gate: ENABLED (waiting for game observations)" : "Learning gate: DISABLED");
            GUILayout.Space(8f);
            GUILayout.Label("LEARNING TARGET");
            GUILayout.Label("Manual label (live player list not connected):");
            string target = GUILayout.TextField(LearningTarget.Value ?? "None", 32);
            if (target != LearningTarget.Value) LearningTarget.Value = string.IsNullOrWhiteSpace(target) ? "None" : target.Trim();

            GUILayout.Space(8f);
            GUILayout.Label("OBJECTIVE PRESET");
            if (GUILayout.Button("Selected: " + ObjectivePreset.Value + " (choose)")) _showObjectiveChoices = !_showObjectiveChoices;
            if (_showObjectiveChoices)
            {
                for (int i = 0; i < ObjectiveOptions.Length; i++)
                {
                    string option = ObjectiveOptions[i];
                    if (GUILayout.Button(option))
                    {
                        ObjectivePreset.Value = option;
                        _showObjectiveChoices = false;
                    }
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("LEARNING STATISTICS");
            GUILayout.Label("Observations saved: " + (_learning == null ? 0 : _learning.ObservationCount));
            GUILayout.Label("Movement patterns: " + (_learning == null ? 0 : _learning.PatternCount));
            GUILayout.Label("Status: " + (_learning == null ? "Unavailable" : _learning.Status));
            GUILayout.Label("Learned pattern summaries:");
            _summaryScroll = GUILayout.BeginScrollView(_summaryScroll, GUILayout.Height(100f));
            GUILayout.Label(_learning == null ? "Unavailable" : _learning.GetSummary(8));
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            GUILayout.Label("DATA SOURCE");
            GUILayout.Label("No Gorilla Tag movement adapter is connected yet.");
            GUILayout.Label("No samples are created until a verified adapter submits them.");
            GUILayout.Label("Only Self or ConsentingTarget samples are accepted.");
            GUILayout.Space(8f);
            if (GUILayout.Button("Hide menu (F8 to reopen)")) _showWindow = false;
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 25f));
        }
    }
}
