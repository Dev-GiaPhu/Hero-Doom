using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Game settings with Inspector-authored UI references. If no panel is assigned,
/// Escape opens a small functional fallback overlay so settings are still usable.
/// </summary>
public sealed class GameSettingsController : MonoBehaviour
{
    [Header("Optional Authored Settings UI")]
    public GameObject settingsPanel;
    public Slider masterVolumeSlider;
    public Slider musicVolumeSlider;
    public Slider sfxVolumeSlider;
    public Toggle fullscreenToggle;
    public TMP_Dropdown qualityDropdown;
    public TMP_Dropdown frameRateDropdown;
    public Button closeButton;

    [Header("Behaviour")]
    public bool escapeTogglesSettings = true;
    public bool pauseGameWhenOpen = true;
    public bool useFallbackOverlayWhenNoPanel = true;

    [Header("Defaults")]
    public bool defaultFullscreen = true;
    [Tooltip("0 = 30, 1 = 60, 2 = 120, 3 = Unlimited")]
    [Range(0, 3)] public int defaultFrameRateOption = 1;

    private GameAudioController audioController;
    private bool fallbackOpen;

    public bool IsOpen => settingsPanel != null ? settingsPanel.activeSelf : fallbackOpen;

    private void Awake()
    {
        audioController = GetComponent<GameAudioController>();
        if (audioController == null)
            audioController = FindFirstObjectByType<GameAudioController>();

        ApplySavedVideoSettings();
        SyncUiFromSettings();
        BindUi();

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void OnDestroy()
    {
        UnbindUi();
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void Update()
    {
        if (escapeTogglesSettings &&
            Keyboard.current != null &&
            Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleSettings();
        }
    }

    public void ToggleSettings()
    {
        SetOpen(!IsOpen);
    }

    public void OpenSettings() => SetOpen(true);
    public void CloseSettings() => SetOpen(false);

    private void SetOpen(bool open)
    {
        fallbackOpen = settingsPanel == null && open;

        if (settingsPanel != null)
        {
            settingsPanel.SetActive(open);
            if (open) settingsPanel.transform.SetAsLastSibling();
        }

        if (pauseGameWhenOpen)
        {
            Time.timeScale = open ? 0f : 1f;
            AudioListener.pause = open;
        }

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (open)
        {
            SyncUiFromSettings();
            GameAudioController.Instance?.PlayClick();
        }
    }

    private void BindUi()
    {
        masterVolumeSlider?.onValueChanged.AddListener(SetMasterVolume);
        musicVolumeSlider?.onValueChanged.AddListener(SetMusicVolume);
        sfxVolumeSlider?.onValueChanged.AddListener(SetSfxVolume);
        fullscreenToggle?.onValueChanged.AddListener(SetFullscreen);
        qualityDropdown?.onValueChanged.AddListener(SetQuality);
        frameRateDropdown?.onValueChanged.AddListener(SetFrameRateOption);
        closeButton?.onClick.AddListener(CloseSettings);
    }

    private void UnbindUi()
    {
        masterVolumeSlider?.onValueChanged.RemoveListener(SetMasterVolume);
        musicVolumeSlider?.onValueChanged.RemoveListener(SetMusicVolume);
        sfxVolumeSlider?.onValueChanged.RemoveListener(SetSfxVolume);
        fullscreenToggle?.onValueChanged.RemoveListener(SetFullscreen);
        qualityDropdown?.onValueChanged.RemoveListener(SetQuality);
        frameRateDropdown?.onValueChanged.RemoveListener(SetFrameRateOption);
        closeButton?.onClick.RemoveListener(CloseSettings);
    }

    private void ApplySavedVideoSettings()
    {
        bool fullscreen = PlayerPrefs.GetInt("HeroDoom.Video.Fullscreen", defaultFullscreen ? 1 : 0) != 0;
        int quality = PlayerPrefs.GetInt("HeroDoom.Video.Quality", QualitySettings.GetQualityLevel());
        int fps = PlayerPrefs.GetInt("HeroDoom.Video.Fps", defaultFrameRateOption);

        Screen.fullScreen = fullscreen;

        if (QualitySettings.names.Length > 0)
            QualitySettings.SetQualityLevel(Mathf.Clamp(quality, 0, QualitySettings.names.Length - 1), true);

        ApplyFrameRate(fps);
    }

    private void SyncUiFromSettings()
    {
        if (audioController == null)
            audioController = FindFirstObjectByType<GameAudioController>();

        if (audioController != null)
        {
            masterVolumeSlider?.SetValueWithoutNotify(audioController.MasterVolume);
            musicVolumeSlider?.SetValueWithoutNotify(audioController.MusicVolume);
            sfxVolumeSlider?.SetValueWithoutNotify(audioController.SfxVolume);
        }

        fullscreenToggle?.SetIsOnWithoutNotify(Screen.fullScreen);

        if (qualityDropdown != null)
        {
            qualityDropdown.ClearOptions();
            qualityDropdown.AddOptions(new System.Collections.Generic.List<string>(QualitySettings.names));
            qualityDropdown.SetValueWithoutNotify(QualitySettings.GetQualityLevel());
        }

        frameRateDropdown?.SetValueWithoutNotify(
            Mathf.Clamp(PlayerPrefs.GetInt("HeroDoom.Video.Fps", defaultFrameRateOption), 0, 3));
    }

    public void SetMasterVolume(float value) => audioController?.SetMasterVolume(value);
    public void SetMusicVolume(float value) => audioController?.SetMusicVolume(value);
    public void SetSfxVolume(float value) => audioController?.SetSfxVolume(value);

    public void SetFullscreen(bool value)
    {
        Screen.fullScreen = value;
        PlayerPrefs.SetInt("HeroDoom.Video.Fullscreen", value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void SetQuality(int value)
    {
        if (QualitySettings.names.Length == 0) return;
        int quality = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
        QualitySettings.SetQualityLevel(quality, true);
        PlayerPrefs.SetInt("HeroDoom.Video.Quality", quality);
        PlayerPrefs.Save();
    }

    public void SetFrameRateOption(int value)
    {
        value = Mathf.Clamp(value, 0, 3);
        ApplyFrameRate(value);
        PlayerPrefs.SetInt("HeroDoom.Video.Fps", value);
        PlayerPrefs.Save();
    }

    private static void ApplyFrameRate(int option)
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = option switch
        {
            0 => 30,
            1 => 60,
            2 => 120,
            _ => -1
        };
    }

    private void OnGUI()
    {
        if (!fallbackOpen || settingsPanel != null || !useFallbackOverlayWhenNoPanel)
            return;

        const float width = 430f;
        const float height = 390f;
        Rect rect = new((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);

        GUILayout.BeginArea(rect, GUI.skin.window);
        GUILayout.Label("HERO DOOM - SETTINGS", GUI.skin.box);

        float master = audioController != null ? audioController.MasterVolume : 1f;
        float music = audioController != null ? audioController.MusicVolume : .32f;
        float sfx = audioController != null ? audioController.SfxVolume : .8f;

        GUILayout.Label($"Master  {Mathf.RoundToInt(master * 100f)}%");
        SetMasterVolume(GUILayout.HorizontalSlider(master, 0f, 1f));
        GUILayout.Label($"Music  {Mathf.RoundToInt(music * 100f)}%");
        SetMusicVolume(GUILayout.HorizontalSlider(music, 0f, 1f));
        GUILayout.Label($"SFX  {Mathf.RoundToInt(sfx * 100f)}%");
        SetSfxVolume(GUILayout.HorizontalSlider(sfx, 0f, 1f));

        GUILayout.Space(12f);
        if (GUILayout.Button(Screen.fullScreen ? "Fullscreen: ON" : "Fullscreen: OFF"))
            SetFullscreen(!Screen.fullScreen);

        string quality = QualitySettings.names.Length > 0
            ? QualitySettings.names[Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, QualitySettings.names.Length - 1)]
            : "Default";
        if (GUILayout.Button($"Quality: {quality}"))
            SetQuality((QualitySettings.GetQualityLevel() + 1) % Mathf.Max(1, QualitySettings.names.Length));

        int fps = PlayerPrefs.GetInt("HeroDoom.Video.Fps", defaultFrameRateOption);
        string fpsText = fps switch { 0 => "30", 1 => "60", 2 => "120", _ => "Unlimited" };
        if (GUILayout.Button($"FPS: {fpsText}"))
            SetFrameRateOption((fps + 1) % 4);

        GUILayout.Space(10f);
        if (GUILayout.Button("Close [ESC]"))
            CloseSettings();

        GUILayout.EndArea();
    }
}
