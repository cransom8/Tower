using UnityEngine;

/// <summary>
/// Runtime bridge to the ToonLit ShaderGraph material properties.
/// Attach to any unit/tower/tile GameObject that needs runtime color changes.
///
/// ShaderGraph exposed properties (add these in the Blackboard):
///   _BaseColor      (Color)    — albedo tint
///   _RimColor       (Color)    — fresnel rim light color
///   _RimPower       (Float)    — rim falloff (default 3.0)
///   _OutlineWidth   (Float)    — outline thickness passed to the outline pass
///   _DebuffTint     (Color)    — purple tint applied when tower is under Warlock debuff
///   _DebuffStrength (Float)    — 0..1 lerp from base to debuff color
///
/// Each Renderer gets a MaterialPropertyBlock so no material instancing is needed.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class ToonShaderBridge : MonoBehaviour
{
    // ── Shader property IDs (cached for perf) ────────────────────────────────
    static readonly int ID_BaseColor      = Shader.PropertyToID("_BaseColor");
    static readonly int ID_RimColor       = Shader.PropertyToID("_RimColor");
    static readonly int ID_RimPower       = Shader.PropertyToID("_RimPower");
    static readonly int ID_OutlineWidth   = Shader.PropertyToID("_OutlineWidth");
    static readonly int ID_DebuffTint     = Shader.PropertyToID("_DebuffTint");
    static readonly int ID_DebuffStrength = Shader.PropertyToID("_DebuffStrength");

    // ── Inspector defaults ───────────────────────────────────────────────────
    [Header("Base")]
    public Color baseColor  = Color.white;
    public Color rimColor   = new Color(1f, 0.85f, 0.2f);  // gold rim
    public float rimPower   = 3f;
    public float outlineWidth = 0.02f;

    [Header("Debuff (Warlock)")]
    public Color debuffTint = new Color(0.6f, 0.1f, 0.9f); // purple

    // ── Internal ─────────────────────────────────────────────────────────────
    Renderer               _renderer;
    MaterialPropertyBlock  _block;
    float                  _debuffStrength;
    float                  _debuffTarget;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _renderer = GetComponent<Renderer>();
        _block    = new MaterialPropertyBlock();
        ApplyAll();
    }

    void Update()
    {
        // Smooth debuff tint transition
        if (!Mathf.Approximately(_debuffStrength, _debuffTarget))
        {
            _debuffStrength = Mathf.MoveTowards(_debuffStrength, _debuffTarget, Time.deltaTime * 4f);
            _block.SetFloat(ID_DebuffStrength, _debuffStrength);
            _renderer.SetPropertyBlock(_block);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Apply Warlock debuff visual (purple tint).</summary>
    public void SetDebuffed(bool active) => _debuffTarget = active ? 1f : 0f;

    /// <summary>Override the unit/tower tint at runtime (e.g. team colour).</summary>
    public void SetBaseColor(Color c)
    {
        baseColor = c;
        _block.SetColor(ID_BaseColor, c);
        _renderer.SetPropertyBlock(_block);
    }

    /// <summary>Set rim color (e.g. gold for player team, red for enemy).</summary>
    public void SetRimColor(Color c)
    {
        rimColor = c;
        _block.SetColor(ID_RimColor, c);
        _renderer.SetPropertyBlock(_block);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    void ApplyAll()
    {
        _renderer.GetPropertyBlock(_block);
        _block.SetColor(ID_BaseColor,      baseColor);
        _block.SetColor(ID_RimColor,       rimColor);
        _block.SetFloat(ID_RimPower,       rimPower);
        _block.SetFloat(ID_OutlineWidth,   outlineWidth);
        _block.SetColor(ID_DebuffTint,     debuffTint);
        _block.SetFloat(ID_DebuffStrength, 0f);
        _renderer.SetPropertyBlock(_block);
    }
}
