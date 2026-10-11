using UnityEngine;

/// <summary>
/// Central audio routing for Hero Doom. Clips are authored in Inspector and fall back to Resources/SFX.
/// It also persists Master/Music/SFX volume and throttles dense combat sounds.
/// </summary>
public sealed class GameAudioController : MonoBehaviour
{
    public static GameAudioController Instance { get; private set; }

    [Header("Authored Audio Clips")]
    [Tooltip("Resources fallback: SFX/BG music")] public AudioClip backgroundMusic;
    [Tooltip("Resources fallback: SFX/Click UI")] public AudioClip clickSound;
    [Tooltip("Resources fallback: SFX/Sword Hit")] public AudioClip meleeHitSound;
    [Tooltip("Resources fallback: SFX/Magic")] public AudioClip magicSound;
    [Tooltip("Resources fallback: SFX/Magic 1 + Magic 2")] public AudioClip[] magicVariants;
    [Tooltip("Resources fallback: summon whoosh")] public AudioClip summonSound;
    [Tooltip("Resources fallback: SFX/SkeletonDie")] public AudioClip monsterDeathSound;
    [Tooltip("Resources fallback: SFX/Level Up")] public AudioClip rewardSound;
    [Tooltip("Resources fallback: SFX/NoMoney")] public AudioClip noMoneySound;

    [Header("Optional Authored Sources")]
    public AudioSource musicSource;
    public AudioSource sfxSource;

    [Header("Player Mix")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = .32f;
    [Range(0f, 1f)] public float sfxVolume = .8f;

    [Header("Category Mix")]
    [Range(0f, 1f)] public float uiVolume = .55f;
    [Range(0f, 1f)] public float combatVolume = .48f;
    [Range(0f, 1f)] public float rewardVolume = .62f;

    [Header("Combat Sound Density")]
    [Range(.02f, .4f)] public float meleeCooldown = .08f;
    [Range(.02f, .5f)] public float magicCooldown = .13f;
    [Range(.02f, .5f)] public float deathCooldown = .09f;

    private float nextMeleeAt;
    private float nextMagicAt;
    private float nextDeathAt;

    public float MasterVolume => masterVolume;
    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        LoadAuthoredDefaults();
        EnsureSources();
        LoadPlayerMix();
        ApplyMix();
        StartBackgroundMusic();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void LoadAuthoredDefaults()
    {
        backgroundMusic ??= Resources.Load<AudioClip>("SFX/BG music");
        clickSound ??= Resources.Load<AudioClip>("SFX/Click UI");
        meleeHitSound ??= Resources.Load<AudioClip>("SFX/Sword Hit");
        magicSound ??= Resources.Load<AudioClip>("SFX/Magic");
        summonSound ??= Resources.Load<AudioClip>("SFX/floraphonic-metal-whoosh-hit-6-201904");
        monsterDeathSound ??= Resources.Load<AudioClip>("SFX/SkeletonDie");
        rewardSound ??= Resources.Load<AudioClip>("SFX/Level Up");
        noMoneySound ??= Resources.Load<AudioClip>("SFX/NoMoney");

        if (magicVariants == null || magicVariants.Length == 0)
        {
            magicVariants = new[]
            {
                Resources.Load<AudioClip>("SFX/Magic 1"),
                Resources.Load<AudioClip>("SFX/Magic 2")
            };
        }
    }

    private void EnsureSources()
    {
        AudioSource[] sources = GetComponents<AudioSource>();

        if (musicSource == null)
            musicSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();

        if (sfxSource == null)
            sfxSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;

        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.volume = 1f;
    }

    private void LoadPlayerMix()
    {
        masterVolume = PlayerPrefs.GetFloat("HeroDoom.Audio.Master", masterVolume);
        musicVolume = PlayerPrefs.GetFloat("HeroDoom.Audio.Music", musicVolume);
        sfxVolume = PlayerPrefs.GetFloat("HeroDoom.Audio.Sfx", sfxVolume);
    }

    private void ApplyMix()
    {
        if (musicSource != null)
            musicSource.volume = Mathf.Clamp01(masterVolume * musicVolume);
    }

    private void StartBackgroundMusic()
    {
        if (musicSource == null || backgroundMusic == null) return;

        if (musicSource.clip != backgroundMusic)
            musicSource.clip = backgroundMusic;

        if (!musicSource.isPlaying && masterVolume * musicVolume > 0f)
            musicSource.Play();
    }

    private void Play(AudioClip clip, float categoryVolume)
    {
        if (clip == null || sfxSource == null) return;

        float volume = Mathf.Clamp01(masterVolume * sfxVolume * categoryVolume);
        if (volume > 0.001f)
            sfxSource.PlayOneShot(clip, volume);
    }

    public void SetMasterVolume(float value)
    {
        masterVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("HeroDoom.Audio.Master", masterVolume);
        PlayerPrefs.Save();
        ApplyMix();
    }

    public void SetMusicVolume(float value)
    {
        musicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("HeroDoom.Audio.Music", musicVolume);
        PlayerPrefs.Save();
        ApplyMix();
        StartBackgroundMusic();
    }

    public void SetSfxVolume(float value)
    {
        sfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat("HeroDoom.Audio.Sfx", sfxVolume);
        PlayerPrefs.Save();
    }

    public void PlayClick() => Play(clickSound, uiVolume);
    public void PlayNoMoney() => Play(noMoneySound, uiVolume);
    public void PlaySummon() => Play(summonSound, combatVolume);
    public void PlayReward() => Play(rewardSound, rewardVolume);

    public void PlayMeleeHit()
    {
        if (Time.unscaledTime < nextMeleeAt) return;
        nextMeleeAt = Time.unscaledTime + meleeCooldown;
        Play(meleeHitSound, combatVolume);
    }

    public void PlayMagic()
    {
        if (Time.unscaledTime < nextMagicAt) return;
        nextMagicAt = Time.unscaledTime + magicCooldown;

        AudioClip clip = magicSound;
        if (magicVariants != null && magicVariants.Length > 0 && Random.value > .38f)
        {
            AudioClip variant = magicVariants[Random.Range(0, magicVariants.Length)];
            if (variant != null) clip = variant;
        }

        Play(clip, combatVolume);
    }

    public void PlayMonsterDeath()
    {
        if (Time.unscaledTime < nextDeathAt) return;
        nextDeathAt = Time.unscaledTime + deathCooldown;
        Play(monsterDeathSound, combatVolume);
    }
}
