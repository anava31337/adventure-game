//using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;

public class AbstractCharacter : MonoBehaviour, IDamageable
{
    #region Variables
    public List<AbstractItem> Inventory = new List<AbstractItem>(new AbstractItem[20]);
    protected CSVReader data;
    #endregion

    #region Properties
    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Mp { get; set; }
    public int MaxMp { get; set; }
    public int Endurance { get; set; }
    public int MaxEndurance { get; set; }
    public int Stamina { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int SwordLevel { get; set; }
    public int WhipLevel { get; set; }
    public int BowLevel { get; set; }
    public int ExpToLvlUp { get; set; }
    public int CoinPurse { get; set; }

    /// <summary>Current character level. Starts at 1.</summary>
    public int Level { get; set; } = 1;

    /// <summary>Experience accumulated toward the next level.</summary>
    public int Exp { get; set; }

    /// <summary>
    /// The character that last landed a hit on this one. No longer used to award
    /// experience — the Hero earns it for every enemy death regardless of cause —
    /// but kept for attribution that DOES care who struck the blow, such as kill
    /// quests or crediting a specific weapon's experience.
    /// </summary>
    public AbstractCharacter LastDamagedBy { get; private set; }

    /// <summary>
    /// Experience granted to whoever defeats this character. Zero by default;
    /// Enemy overrides it with a per-prefab value set in the Inspector.
    /// </summary>
    public virtual int ExpReward => 0;
    //public AbstractItem[] Inventory { get; set; }
    //public Skill[] Skills { get; set; }
    //public Spell[] Spells { get; set; }
    public enum StatusEffect { Bleed, Poisoned, Sick, Burn, Heatstroke, Frostbite, Armstrong, Ironflesh };
    public delegate void CharacterAction();
    public delegate void CharacterAction<T>(T action);
    // Two-parameter form, used by OnCharacterDamaged (hpPercent + who was hit).
    // Mirrors the pattern Enemy already used for its own damage event.
    public delegate void CharacterAction<T, T2>(T param1, T2 param2);
    public static event CharacterAction<Int32> OnHpUpdate;
    public static event CharacterAction<Int32> OnCoinUpdate;
    public static event CharacterAction<AbstractCharacter> OnDeath;

    /// <summary>Raised when a character's experience changes. Carries the character.</summary>
    public static event CharacterAction<AbstractCharacter> OnExpChanged;

    /// <summary>Raised when a character's coin purse changes. Carries the character.</summary>
    public static event CharacterAction<AbstractCharacter> OnCoinsChanged;

    /// <summary>
    /// Raised each time a character gains a level. The hook for the level-up
    /// screen where the player chooses HP, ATK, DEF or EN to raise.
    /// </summary>
    public static event CharacterAction<AbstractCharacter> OnLeveledUp;

    #endregion

    #region MonoBehaviour


    // =========================================================================
    // Resource changes — flat amounts and percentages
    //
    // Every resource has an int overload (a flat amount) and a float overload
    // (a fraction of the MAXIMUM). The pair exists because items naturally come
    // in both kinds: a small potion restores exactly 20 HP and stays a modest
    // heal forever, while an elixir restores 50% and remains meaningful as the
    // character's maximum grows. Expressing the second as a flat number would
    // mean rebalancing every such item on every max-HP increase.
    //
    // Percentages are always OF THE MAXIMUM, never of what remains, so "heal
    // 25%" restores a predictable amount rather than progressively less the more
    // hurt you are.
    // =========================================================================

    /// <summary>
    /// Grants experience and handles any level-ups it causes. Leftover experience
    /// carries over, so a large reward can cross several levels at once.
    /// </summary>
    public void AddExp(int amount)
    {
        if (amount <= 0) return;
        Exp += amount;

        // Guard against a zero threshold, which would loop forever.
        while (ExpToLvlUp > 0 && Exp >= ExpToLvlUp)
        {
            Exp  -= ExpToLvlUp;
            Level++;
            OnLeveledUp?.Invoke(this);
        }

        OnExpChanged?.Invoke(this);
    }

    /// <summary>Adds (or, with a negative amount, spends) coins, clamped 0–9999.</summary>
    public void AddCoins(int amount)
    {
        if (amount == 0) return;
        CoinPurse = Mathf.Clamp(CoinPurse + amount, 0, MaxCoins);
        OnCoinsChanged?.Invoke(this);
    }

    /// <summary>Coin purse cap.</summary>
    public const int MaxCoins = 9999;

    /// <summary>Restores a flat amount of HP, clamped to MaxHp.</summary>
    public void RestoreHp(int amount)
    {
        if (amount == 0) return;
        Hp = Mathf.Clamp(Hp + amount, 0, MaxHp);
        RefreshHealthDisplay();
    }

    /// <summary>Restores a fraction of MAXIMUM HP. 0.25f = a quarter of max.</summary>
    public void RestoreHp(float fractionOfMax)
    {
        RestoreHp(Mathf.RoundToInt(MaxHp * fractionOfMax));
    }

    public void RestoreMp(int amount)
    {
        if (amount == 0) return;
        Mp = Mathf.Clamp(Mp + amount, 0, MaxMp);
    }

    public void RestoreMp(float fractionOfMax)
    {
        RestoreMp(Mathf.RoundToInt(MaxMp * fractionOfMax));
    }

    /// <summary>
    /// Restores endurance. Stamina is capped by CURRENT endurance, so raising
    /// endurance raises the ceiling stamina may regenerate to.
    /// </summary>
    public void RestoreEndurance(int amount)
    {
        if (amount == 0) return;
        Endurance = Mathf.Clamp(Endurance + amount, 0, MaxEndurance);
    }

    public void RestoreEndurance(float fractionOfMax)
    {
        RestoreEndurance(Mathf.RoundToInt(MaxEndurance * fractionOfMax));
    }

    /// <summary>Restores stamina, clamped to CURRENT endurance rather than max.</summary>
    public void RestoreStamina(int amount)
    {
        if (amount == 0) return;
        Stamina = Mathf.Clamp(Stamina + amount, 0, Endurance);
    }

    public void RestoreStamina(float fractionOfEndurance)
    {
        RestoreStamina(Mathf.RoundToInt(Endurance * fractionOfEndurance));
    }

    /// <summary>
    /// Damage as a fraction of MAXIMUM HP, routed through the normal pipeline so
    /// resistances, invulnerability, knockback and the hit flash all still apply.
    /// Useful for percentage-based hazards that stay dangerous at any level.
    /// </summary>
    public void TakeDamage(float fractionOfMaxHp, DamageType type = DamageType.Physical)
    {
        int amount = Mathf.Max(1, Mathf.RoundToInt(MaxHp * fractionOfMaxHp));
        TakeDamage(new DamageInfo(type, amount));
    }

    /// <summary>Spends stamina if there is enough. Returns false and spends nothing otherwise.</summary>
    public bool TrySpendStamina(int amount)
    {
        if (amount <= 0) return true;
        if (Stamina < amount) return false;
        Stamina -= amount;
        return true;
    }

    /// <summary>Spends a fraction of CURRENT endurance worth of stamina.</summary>
    public bool TrySpendStamina(float fractionOfEndurance)
    {
        return TrySpendStamina(Mathf.RoundToInt(Endurance * fractionOfEndurance));
    }

    // =========================================================================
    // Character gravity — one shared model for every character in the game
    //
    // Previously each character got gravity from whatever component happened to
    // be attached, which produced three different behaviours:
    //   • the Player used manual gravity in CharacterController2D
    //   • Scuttler used Unity's built-in gravity (gravityScale 90)
    //   • Landlouse had neither, and floated
    // There was no single place that said "characters fall", so whether an enemy
    // obeyed gravity depended on its component list — and a stray scene override
    // could silently disable it with nothing to point at.
    //
    // Now every AbstractCharacter falls by default, using the SAME numbers as the
    // player. The one exception is a character that has a CharacterController2D:
    // that controller already owns gravity, entangled with jump arcs, coyote
    // time, knockback and platform riding, so this stands down for it.
    // =========================================================================

    [Header("Gravity")]
    [Tooltip("Turn off only for characters that should never fall — a floating " +
             "ghost, a wall-mounted turret, a purely decorative NPC.")]
    public bool useGravity = true;

    [Tooltip("Gravity strength, multiplied by Physics2D.gravity. Matches the " +
             "player's value so every character falls at the same rate.")]
    public float gravityMultiplier = 100f;

    [Tooltip("Extra gravity while descending. 1 = symmetric arc, matching the " +
             "player's current setting.")]
    public float fallGravityMultiplier = 1f;

    [Tooltip("Terminal fall speed (px/s).")]
    public float maxFallSpeed = 1200f;

    [Tooltip("Layers this character can stand on.")]
    public LayerMask groundLayer;

    [Tooltip("Extra reach on the ground sweep (px).")]
    public float groundSkin = 2f;

    /// <summary>
    /// Set by launch effects (SpawnLaunch, a scripted toss) that need to own
    /// vertical motion for a moment. Gravity resumes automatically afterwards.
    /// </summary>
    public bool GravitySuspended { get; set; }

    /// <summary>True while resting on ground. Meaningful only when this class owns gravity.</summary>
    public bool IsOnGround { get; private set; }

    private Rigidbody2D _body;
    private Collider2D  _bodyCollider;
    private bool        _controllerOwnsGravity;

    private void InitGravity()
    {
        _body = GetComponent<Rigidbody2D>();

        // Prefer a solid collider for ground tests; fall back to any collider so
        // characters built with only a trigger still land correctly.
        foreach (var c in GetComponents<Collider2D>())
        {
            if (_bodyCollider == null) _bodyCollider = c;
            if (!c.isTrigger) { _bodyCollider = c; break; }
        }

        // The player's controller owns gravity; everything else falls through here.
        _controllerOwnsGravity = GetComponent<CharacterController2D>() != null;

        // Unity's built-in gravity is switched off so there is exactly ONE source
        // of falling. This also makes stray gravityScale values on prefabs or
        // scene instances irrelevant instead of silently breaking a character.
        if (_body != null && !_controllerOwnsGravity)
            _body.gravityScale = 0f;
    }

    protected virtual void FixedUpdate()
    {
        ApplyCharacterGravity();
    }

    private void ApplyCharacterGravity()
    {
        if (_controllerOwnsGravity) return;      // CharacterController2D handles it
        if (!useGravity || GravitySuspended) return;
        if (_body == null || _body.bodyType != RigidbodyType2D.Dynamic) return;

        // Integrate gravity using the same model as the player.
        float mult  = _body.velocity.y < 0f ? fallGravityMultiplier : 1f;
        float newVY = _body.velocity.y - Mathf.Abs(Physics2D.gravity.y) * gravityMultiplier
                                       * mult * Time.fixedDeltaTime;
        newVY = Mathf.Max(newVY, -maxFallSpeed);
        _body.velocity = new Vector2(_body.velocity.x, newVY);

        // Land, using a swept test so a fast fall cannot tunnel through the floor.
        IsOnGround = false;
        if (_body.velocity.y <= 0f && _bodyCollider != null)
        {
            float travel = Mathf.Abs(_body.velocity.y) * Time.fixedDeltaTime + groundSkin;
            Vector2 origin = new Vector2(_bodyCollider.bounds.center.x, _bodyCollider.bounds.min.y);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, travel, groundLayer);

            if (hit.collider != null)
            {
                // Snap exactly onto the surface so characters rest on the ground
                // rather than a pixel or two above or inside it.
                float delta = hit.point.y - _bodyCollider.bounds.min.y;
                _body.position = new Vector2(_body.position.x, _body.position.y + delta);
                _body.velocity = new Vector2(_body.velocity.x, 0f);
                IsOnGround = true;
            }
        }
    }

    /// <summary>Applies an upward impulse — hops, launches, springs.</summary>
    public void AddVerticalImpulse(float velocity)
    {
        if (_body == null) return;
        _body.velocity = new Vector2(_body.velocity.x, velocity);
        IsOnGround = false;
    }

    protected virtual void Start()
    {
        InitGravity();

        if (this.GetComponent<SpriteRenderer>() != null && MapManager.Instance.map != null)
        {
            this.GetComponent<SpriteRenderer>().sortingOrder = MapManager.Instance.groundLayerID;
        }
    }

    // Update is called once per frame
    void Update()
    {

    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        if (collider.GetComponent<TerrainVolume>() != null)
        {
            /*
            if (OnWaterContact != null)
            {
                OnWaterContact(1);
            }
            if (collider.GetComponent<ColorBlinker>() != null)
            {
                collider.GetComponent<ColorBlinker>().enabled = true;
            }
            */
            Debug.Log("WATER!");
        }
    }
    #endregion

    #region Methods
    /// <summary>
    /// Loads this character's stats for a level from its CSV.
    ///
    /// Each stat is read through ReadStat, which names the exact row and column
    /// on failure. A missing row used to throw an IndexOutOfRangeException that
    /// aborted the character's Start() partway through — leaving HP, MP and
    /// endurance at zero with no hint why, and surfacing much later as empty
    /// HUD bars. Now a short or malformed file logs precisely what is wrong and
    /// the remaining stats still load.
    /// </summary>
    protected void SetLevelData(int Level)
    {
        data = this.GetComponent<CSVReader>();

        if (data == null || data.grid == null)
        {
            Debug.LogError($"[{name}] SetLevelData: no CSVReader, or its CSV failed to " +
                           "load. Assign a stats CSV to the CSVReader component.", this);
            return;
        }

        this.MaxHp        = ReadStat(Level, 1, "MaxHP");
        this.MaxMp        = ReadStat(Level, 2, "MaxMP");
        this.MaxEndurance = ReadStat(Level, 3, "MaxEndurance");
        // Stamina's ceiling is CURRENT endurance, so it is seeded from that
        // rather than from a separate maximum.
        this.Stamina      = this.MaxEndurance;
        this.Attack       = ReadStat(Level, 4, "Attack");
        this.Defense      = ReadStat(Level, 5, "Defense");
        this.SwordLevel   = ReadStat(Level, 6, "SwordLevel");
        this.WhipLevel    = ReadStat(Level, 7, "WhipLevel");
        this.BowLevel     = ReadStat(Level, 8, "BowLevel");
        this.ExpToLvlUp   = ReadStat(Level, 9, "ExpToLevelUp");
    }

    /// <summary>
    /// Reads one integer stat, returning 0 and logging a specific warning if the
    /// row or column is missing, or the cell isn't a number. The grid is indexed
    /// [column, row]: the column is the level, the row is the stat.
    /// </summary>
    private int ReadStat(int level, int row, string statName)
    {
        string file = (data.csvFile != null) ? data.csvFile.name : "(unnamed CSV)";

        int cols = data.grid.GetLength(0);
        int rows = data.grid.GetLength(1);

        if (row >= rows)
        {
            Debug.LogWarning($"[{name}] {file}.csv has no row {row} for '{statName}' " +
                             $"(it has {rows} rows, 0-{rows - 1}). Add a '{statName}' row " +
                             "in that position, or the stat defaults to 0.", this);
            return 0;
        }
        if (level >= cols)
        {
            Debug.LogWarning($"[{name}] {file}.csv has no column for level {level} " +
                             $"on row '{statName}'. Defaulting to 0.", this);
            return 0;
        }

        string cell = data.grid[level, row];
        if (string.IsNullOrWhiteSpace(cell))
        {
            Debug.LogWarning($"[{name}] {file}.csv row '{statName}' is empty for " +
                             $"level {level}. Defaulting to 0.", this);
            return 0;
        }

        if (!int.TryParse(cell.Trim(), out int value))
        {
            Debug.LogWarning($"[{name}] {file}.csv row '{statName}', level {level}: " +
                             $"'{cell}' is not a whole number. Defaulting to 0.", this);
            return 0;
        }
        return value;
    }

    public void SetAnim(string animName)
    {
        if (this.GetComponent<Animator>() != null)
        {
            Animator anim = this.GetComponent<Animator>();
            IEnumerable<string> state = from s in anim.parameters where s.name != animName select s.name;

            foreach (string s in state)
            {
                anim.SetBool(s, false);
            }
            anim.SetBool(animName, true);
        }
    }

    // =========================================================================
    // IDamageable
    // =========================================================================

    [Header("Damage Response")]
    [Tooltip("Seconds of invulnerability granted after being hit — the blink window. " +
             "During it the character ignores all further damage and knockback.\n\n" +
             "NOTE: this field was added after some prefabs were created, so an old " +
             "prefab may have it serialized as 0, which silently disables i-frames " +
             "and lets contact damage land every few frames. If a character takes " +
             "damage far too rapidly, check this value first.")]
    public float invulnerabilityDuration = 0.8f;

    public bool       IsAlive          => this.Hp > 0;
    public GameObject DamageableObject => this.gameObject;

    /// <summary>True while this character is in its post-hit invulnerability window.</summary>
    public bool IsInvulnerable => _invulnerableUntil > Time.time;

    /// <summary>True while knocked down and unable to act.</summary>
    public bool IsKnockedDown => _knockdownUntil > Time.time;

    private float _invulnerableUntil;
    private float _knockdownUntil;

    /// <summary>Fired on this character whenever it is damaged, with what landed.
    /// Reaction components (catch fire, freeze, stagger) subscribe to this.</summary>
    public event System.Action<DamageInfo, Dictionary<DamageType, int>> OnDamaged;

    /// <summary>
    /// Fired whenever ANY character takes damage, carrying the new health
    /// percentage and who was hit. Child HP-bar objects subscribe to this and
    /// filter by their parent, so enemies and NPCs share one implementation.
    /// </summary>
    public static event CharacterAction<Int32, AbstractCharacter> OnCharacterDamaged;

    /// <summary>
    /// The single damage entry point for every character in the game.
    /// Applies resistances, respects invulnerability, subtracts HP, applies
    /// knockback/knockdown, and reports the new health percentage.
    /// </summary>
    public virtual void TakeDamage(DamageInfo info)
    {
        if (info == null || !IsAlive) return;

        // Post-hit invulnerability — unless this hit is designed to pierce it.
        if (IsInvulnerable && !info.ignoresInvulnerability) return;

        // Remember who hit us, so the kill can be credited if this hit is fatal.
        if (info.instigator != null && info.instigator != this)
            LastDamagedBy = info.instigator;

        // Resistances are optional; a character without the component takes
        // everything at full strength.
        Dictionary<DamageType, int> applied;
        int total = DamageResistances.Resolve(gameObject, info, out applied);

        if (total <= 0) return;   // fully resisted — nothing landed at all

        this.Hp -= total;

        // ── Impact: knockback and knockdown ─────────────────────────────────
        if (info.knockback != Vector2.zero)
        {
            // Route through the controller when there is one so it can suspend
            // input for a moment. Setting rb.velocity directly is not enough:
            // the controller's near-instant acceleration would erase the impulse
            // on the very next physics step.
            var cc = GetComponent<CharacterController2D>();
            if (cc != null)
            {
                cc.ApplyKnockback(info.knockback);
            }
            else
            {
                var rb = GetComponent<Rigidbody2D>();
                if (rb != null) rb.velocity = info.knockback;
            }
        }

        if (info.knockdownDuration > 0f)
        {
            // A knockdown deliberately CONSUMES the i-frame window: being floored
            // by a heavy blow should leave you exposed, not protected.
            _knockdownUntil    = Time.time + info.knockdownDuration;
            _invulnerableUntil = 0f;
        }
        else if (invulnerabilityDuration > 0f)
        {
            _invulnerableUntil = Time.time + invulnerabilityDuration;
        }

        // ── Hit feedback ────────────────────────────────────────────────────
        // Enabling ColorBlinker restarts its blink coroutine (it runs from
        // OnEnable and disables itself when finished). This lives here rather
        // than in Enemy so EVERY character — player, enemy, NPC — flashes when
        // hit, from ANY damage source: sword, whip, arrow, or contact.
        var blinker = GetComponent<ColorBlinker>();
        if (blinker != null) blinker.enabled = true;

        // ── Report ──────────────────────────────────────────────────────────
        OnDamaged?.Invoke(info, applied);

        if (this.Hp <= 0)
        {
            BroadcastHealth(0);

            // Null-conditional: this previously threw a NullReferenceException
            // whenever a character died with nothing subscribed to OnDeath.
            OnDeath?.Invoke(this);
            Destroy(this.gameObject);
            return;
        }

        BroadcastHealth(HealthPercent);
    }

    /// <summary>Current health as a 0–100 percentage.</summary>
    public int HealthPercent =>
        MaxHp > 0 ? Mathf.Clamp((int)(((float)Hp / (float)MaxHp) * 100f), 0, 100) : 0;

    /// <summary>
    /// Announces the new health percentage so HP bars can update.
    /// The default fires OnCharacterDamaged, which the HP-bar object parented to
    /// this character listens for — correct for Enemies and NPCs alike.
    /// Player overrides this because its bar lives on the HUD canvas instead.
    /// </summary>
    protected virtual void BroadcastHealth(int hpPercent)
    {
        OnCharacterDamaged?.Invoke(hpPercent, this);
    }

    /// <summary>
    /// Pushes the CURRENT health to any listening HP bar without applying damage.
    /// Needed because HP bars only ever update in response to a damage event: a
    /// freshly spawned creature has never been hit, so without this its bar keeps
    /// whatever sprite the prefab happened to be saved with — which is why
    /// spawned children appeared with an empty bar despite being at full health.
    /// Call after setting up a character's stats.
    /// </summary>
    public void RefreshHealthDisplay()
    {
        BroadcastHealth(HealthPercent);
    }

    public int HpUpdate(int value)
    {
        float hpRatio;
        int hpPercent;

        hpRatio = ((float)this.Hp / (float)this.MaxHp) * 100f;
        hpPercent = (int)hpRatio;
        if (OnHpUpdate != null)
        {
            OnHpUpdate(hpPercent);
        }
        if (this.Hp <= 0) //Player/Character Death
        {
            //OnDeath(this);
            Destroy(this.gameObject);
        }
        return this.Hp;
    }

    public int CoinUpdate(int value)
    {

        if (OnCoinUpdate != null)
        {
            OnCoinUpdate(value);
        }
        return this.CoinPurse;
        
    }

    public virtual void ExecuteActiveStatusEffects()
    {
        /*
        foreach (StatusEffect effect in this.StatusEffects)
        {
            Status.Effect(effect);
        }
        */
    }
    #endregion

    #region Interfaces
    
    public interface IStatus
    {
        void Effect(StatusEffect effectName);//AbstractCharacter character)
        void Cure();
    }
    protected class Status
    {
        private static Dictionary<StatusEffect, IStatus> statusLibrary = new Dictionary<StatusEffect, IStatus>();
        //private static List<IStatus> statusIndex = new List<IStatus>();
        static Status()
        {
            statusLibrary.Add(StatusEffect.Poisoned, new Poisoned());
            statusLibrary.Add(StatusEffect.Bleed, new Bleed());
            statusLibrary.Add(StatusEffect.Sick, new Sick());
            statusLibrary.Add(StatusEffect.Burn, new Burn());
            statusLibrary.Add(StatusEffect.Heatstroke, new Heatstroke());
            statusLibrary.Add(StatusEffect.Frostbite, new Frostbite());
            statusLibrary.Add(StatusEffect.Armstrong, new Armstrong());
            statusLibrary.Add(StatusEffect.Ironflesh, new Ironflesh());
        }
        public static void Effect(StatusEffect effectName)
        {
            statusLibrary[effectName].Effect(effectName);
        }
        public static void Cure()
        {

        }
    }
    private class Bleed : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Poisoned : IStatus
    {
        public void Effect(StatusEffect effectName)
        {
            Debug.Log("Im friggin' poisoned! "); // wrks
                                                 //Determine the damage of poison effect here, display damage text
                                                 //Display any visual effects of poison to character / instantiate partile effects
        }
        public void Cure()
        {

        }
    }
    private class Sick : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Burn : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Heatstroke : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Frostbite : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Armstrong : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    private class Ironflesh : IStatus
    {
        public void Effect(StatusEffect effectName)
        {

        }
        public void Cure()
        {

        }
    }
    #endregion
}