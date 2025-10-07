using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Text;

public class DebugConsoleUI : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI logText;
    public Button toggleButton;
    public GameObject consolePanel;

    [Header("Settings")]
    [Tooltip("Maximum number of log lines to keep")]
    public int maxLines = 60;

    private StringBuilder _logBuilder = new StringBuilder();
    private bool _visible = false;
    private int _lineCount = 0;

    private void Awake()
    {
        if (!logText) logText = GetComponentInChildren<TextMeshProUGUI>();
        if (!consolePanel) consolePanel = logText ? logText.transform.parent.gameObject : gameObject;

        Application.logMessageReceived += HandleLog;

        if (toggleButton)
            toggleButton.onClick.AddListener(ToggleConsole);

        Show(false);
    }

    private void OnDestroy()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        string color = type switch
        {
            LogType.Warning => "yellow",
            LogType.Error or LogType.Exception => "red",
            _ => "white"
        };

        _logBuilder.AppendLine($"<color={color}>{logString}</color>");
        _lineCount++;

        // trim buffer if too long
        if (_lineCount > maxLines)
        {
            int idx = _logBuilder.ToString().IndexOf('\n');
            if (idx >= 0) _logBuilder.Remove(0, idx + 1);
            _lineCount--;
        }

        if (logText) logText.text = _logBuilder.ToString();
    }

    public void ToggleConsole()
    {
        Show(!_visible);
    }

    private void Show(bool show)
    {
        _visible = show;
        if (consolePanel) consolePanel.SetActive(show);
    }
}