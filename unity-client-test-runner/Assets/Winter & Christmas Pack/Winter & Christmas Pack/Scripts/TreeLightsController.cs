using UnityEngine;

namespace WinterChristmasVillage
{ 
public class ChristmasLights : MonoBehaviour
{
    [Header("Light Colors (choose your palette)")]
    public Color[] lightColors;

    [Header("Timing")]
    public float minInterval = 0.2f;
    public float maxInterval = 1.0f;

    [Header("Emission")]
    public float emissionIntensity = 6f; // URP needs stronger values

    private Renderer[] lights;

    void Start()
    {
        if (lightColors == null || lightColors.Length == 0)
        {
            Debug.LogWarning("No colors assigned! Please add colors to the palette.");
            return;
        }

        lights = GetComponentsInChildren<Renderer>();
        ChangeLights();
        InvokeRepeating(nameof(ChangeLights), 0, Random.Range(minInterval, maxInterval));
    }

    void ChangeLights()
    {
        foreach (var rend in lights)
        {
            var mat = rend.material;

            // Escolhe cor random da paleta
            Color c = lightColors[Random.Range(0, lightColors.Length)];

            // Albedo
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);

            // Emissive
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                Color emissive = c * emissionIntensity;

                mat.SetColor("_EmissionColor", emissive);

                // URP Refresh
                DynamicGI.SetEmissive(rend, emissive);
            }
        }
    }
}
}
