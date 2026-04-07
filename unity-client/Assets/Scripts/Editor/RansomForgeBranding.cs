#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class RansomForgeBranding
{
    public const string CompanyName = "RansomForge";
    public const string ProductName = "RansomForge";

    public const string AppIconAssetPath = "Assets/Branding/RansomForgeAppIcon.png";
    public const string AdaptiveBackgroundAssetPath = "Assets/Branding/RansomForgeAdaptiveBackground.png";
    public const string AdaptiveForegroundAssetPath = "Assets/Branding/RansomForgeAdaptiveForeground.png";
    public const string PlayStoreIconAssetPath = "Assets/Branding/RansomForgePlayStoreIcon.png";

    const string MenuRoot = "RansomForge/Branding/";

    [MenuItem(MenuRoot + "Apply Android Branding")]
    public static void ApplyAndroidBrandingMenu() => ApplyAndroidBrandingOrThrow();

    [MenuItem(MenuRoot + "Validate Android Branding")]
    public static void ValidateAndroidBrandingMenu()
    {
        ValidateAndroidBrandingOrThrow();
        Debug.Log("[RansomForgeBranding] Android branding is configured correctly.");
    }

    public static void ApplyAndroidBrandingOrThrow()
    {
        Texture2D appIcon = LoadRequiredTexture(AppIconAssetPath);
        Texture2D adaptiveBackground = LoadRequiredTexture(AdaptiveBackgroundAssetPath);
        Texture2D adaptiveForeground = LoadRequiredTexture(AdaptiveForegroundAssetPath);
        LoadRequiredTexture(PlayStoreIconAssetPath);

        PlayerSettings.companyName = CompanyName;
        PlayerSettings.productName = ProductName;

        ApplyAndroidIcons(appIcon, adaptiveBackground, adaptiveForeground);

        AssetDatabase.SaveAssets();
        ValidateAndroidBrandingOrThrow();

        Debug.Log(
            "[RansomForgeBranding] Applied Android branding. " +
            $"AppIcon={AppIconAssetPath} AdaptiveBackground={AdaptiveBackgroundAssetPath} AdaptiveForeground={AdaptiveForegroundAssetPath}");
    }

    public static void ValidateAndroidBrandingOrThrow()
    {
        if (!string.Equals(PlayerSettings.companyName, CompanyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: companyName is '{PlayerSettings.companyName}', expected '{CompanyName}'.");
        }

        if (!string.Equals(PlayerSettings.productName, ProductName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: productName is '{PlayerSettings.productName}', expected '{ProductName}'.");
        }

        Texture2D appIcon = LoadRequiredTexture(AppIconAssetPath);
        Texture2D adaptiveBackground = LoadRequiredTexture(AdaptiveBackgroundAssetPath);
        Texture2D adaptiveForeground = LoadRequiredTexture(AdaptiveForegroundAssetPath);
        LoadRequiredTexture(PlayStoreIconAssetPath);

        ValidateAndroidIcons(appIcon, adaptiveBackground, adaptiveForeground);
    }

    static void ApplyAndroidIcons(Texture2D appIcon, Texture2D adaptiveBackground, Texture2D adaptiveForeground)
    {
        PlatformIconKind[] supportedKinds = GetSupportedAndroidIconKinds();
        int appliedKinds = 0;
        foreach (PlatformIconKind supportedKind in supportedKinds)
        {
            Texture2D[] textures = IsMultiLayerKind(supportedKind)
                ? new[] { adaptiveBackground, adaptiveForeground }
                : new[] { appIcon };
            ApplyIconKind(supportedKind, textures);
            appliedKinds++;
        }

        if (appliedKinds == 0)
        {
            throw new InvalidOperationException(
                "Android branding is invalid: Unity did not expose any Android icon kinds for launcher icons.");
        }
    }

    static void ValidateAndroidIcons(Texture2D appIcon, Texture2D adaptiveBackground, Texture2D adaptiveForeground)
    {
        PlatformIconKind[] supportedKinds = GetSupportedAndroidIconKinds();
        int validatedKinds = 0;
        foreach (PlatformIconKind supportedKind in supportedKinds)
        {
            Texture2D[] textures = IsMultiLayerKind(supportedKind)
                ? new[] { adaptiveBackground, adaptiveForeground }
                : new[] { appIcon };
            ValidateIconKind(supportedKind, textures);
            validatedKinds++;
        }

        if (validatedKinds == 0)
        {
            throw new InvalidOperationException(
                "Android branding is invalid: Unity did not expose any Android icon kinds for launcher icons.");
        }
    }

    static void ApplyIconKind(PlatformIconKind kind, Texture2D[] textures)
    {
        PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        if (slots == null || slots.Length == 0)
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: Unity returned no icon slots for icon kind '{kind}'.");
        }

        foreach (PlatformIcon slot in slots)
        {
            if (slot.minLayerCount > textures.Length || slot.maxLayerCount < textures.Length)
            {
                throw new InvalidOperationException(
                    $"Android branding is invalid: icon kind '{kind}' slot {slot.width}x{slot.height} expects between " +
                    $"{slot.minLayerCount} and {slot.maxLayerCount} layers, but {textures.Length} texture(s) were provided.");
            }

            slot.SetTextures(textures);
        }

        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
    }

    static void ValidateIconKind(PlatformIconKind kind, Texture2D[] expectedTextures)
    {
        PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        if (slots == null || slots.Length == 0)
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: Unity returned no icon slots for icon kind '{kind}'.");
        }

        foreach (PlatformIcon slot in slots)
        {
            Texture2D[] assignedTextures = slot.GetTextures();
            if (assignedTextures == null || assignedTextures.Length < expectedTextures.Length)
            {
                throw new InvalidOperationException(
                    $"Android branding is invalid: icon kind '{kind}' slot {slot.width}x{slot.height} has " +
                    $"{(assignedTextures == null ? 0 : assignedTextures.Length)} layer(s), expected {expectedTextures.Length}.");
            }

            for (int i = 0; i < expectedTextures.Length; i++)
            {
                Texture2D assignedTexture = assignedTextures[i];
                if (assignedTexture == null)
                {
                    throw new InvalidOperationException(
                        $"Android branding is invalid: icon kind '{kind}' slot {slot.width}x{slot.height} is missing layer {i}.");
                }

                string assignedPath = AssetDatabase.GetAssetPath(assignedTexture);
                string expectedPath = AssetDatabase.GetAssetPath(expectedTextures[i]);
                if (!string.Equals(assignedPath, expectedPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Android branding is invalid: icon kind '{kind}' slot {slot.width}x{slot.height} layer {i} " +
                        $"uses '{assignedPath}', expected '{expectedPath}'.");
                }
            }
        }
    }

    static Texture2D LoadRequiredTexture(string assetPath)
    {
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        if (texture == null)
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: required texture asset '{assetPath}' is missing or not imported.");
        }

        return texture;
    }

    static PlatformIconKind[] GetSupportedAndroidIconKinds()
    {
        PlatformIconKind[] supportedKinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
        if (supportedKinds == null || supportedKinds.Length == 0)
        {
            throw new InvalidOperationException("Android branding is invalid: Unity reported no supported Android icon kinds.");
        }

        return supportedKinds;
    }

    static bool IsMultiLayerKind(PlatformIconKind kind)
    {
        PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
        if (slots == null || slots.Length == 0)
        {
            throw new InvalidOperationException(
                $"Android branding is invalid: Unity returned no icon slots for icon kind '{kind}'.");
        }

        foreach (PlatformIcon slot in slots)
        {
            if (slot.maxLayerCount > 1 || slot.minLayerCount > 1)
                return true;
        }

        return false;
    }
}
#endif
