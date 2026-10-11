using UnityEngine;

/// <summary>
/// Central audio routing for Hero Doom. Clips are loaded from the authored Resources/SFX folder.
/// This owns no UI and throttles rapid combat sounds so large battles remain readable.
/// </summary>
public sealed class GameAudioController : MonoBehaviour
{
    public static GameAudioController Instance { get; private set; }

    [Header("Authored SFX (optional overrides)")]
    [Tooltip("Leave empty to use Resources/SFX/BG music.")] public AudioClip backgroundMusic;
    [Tooltip("Leave empty to use Resources/SFX/Click UI.")] public AudioClip clickSound;
    [Tooltip("Leave empty to use Resources/SFX/Sword Hit.")] public AudioClip meleeHitSound;
    [Tooltip("Leave empty to use Resources/SFX/Magic.")] public AudioClip magicSound;
    [Tooltip("Leave empty to use Resources/SFX/Magic 1 and Magic 2 as variants.")] public AudioClip[] magicVariants;
    [Tooltip("Leave empty to use Resources/SFX/floraphonic-metal-whoosh-hit-6-201904.")] public AudioClip summonSound;
    [Tooltip("Leave empty to use Resources/SFX/SkeletonDie.")] public AudioClip monsterDeathSound;
    [Tooltip("Leave empty to use Resources/SFX/Level Up.")] public AudioClip rewardSound;
    [Tooltip("Leave empty to use Resources/SFX/NoMoney.")] public AudioClip noMoneySound;

    [Header("Mix")]
    [Range(0f, 1f)] public float musicVolume = .32f;
    [Range(0f, 1f)] public float uiVolume = .55f;
    [Range(0f, 1f)] public float combatVolume = .48f;
    [Range(0f, 1f)] public float rewardVolume = .62f;
    [Tooltip("Minimum time between global melee sounds.")] [Range(.02f, .4f)] public float meleeCooldown = .08f;
    [Tooltip("Minimum time between global magic sounds.")] [Range(.02f, .5f)] public float magicCooldown = .13f;
    [Tooltip("Minimum time between global death sounds.")] [Range(.02f, .5f)] public float deathCooldown = .09f;

    private AudioSource musicSource;
    private AudioSource sfxSource;
    private float nextMeleeAt;
    private float nextMagicAt;
    private float nextDeathAt;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        LoadAuthoredDefaults();
        CreateSources();
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
            magicVariants = new[] { Resources.Load<AudioClip>("SFX/Magic 1"), Resources.Load<AudioClip>("SFX/Magic 2") };
    }

    private void CreateSources()
    {
        AudioSource[] sources = GetComponents<AudioSource>();
        musicSource = sources.Length > 0 ? sources[0] : gameObject.AddComponent<AudioSource>();
        sfxSource = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
    }

    private void StartBackgroundMusic()
    {
        if (backgroundMusic == null || musicVolume <= 0f || musicSource.isPlaying) return;
        musicSource.clip = backgroundMusic;
        musicSource.volume = musicVolume;
        musicSource.Play();
    }

    private void Play(AudioClip clip, float volume)
    {
        if (clip != null && sfxSource != null && volume > 0f) sfxSource.PlayOneShot(clip, volume);
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
