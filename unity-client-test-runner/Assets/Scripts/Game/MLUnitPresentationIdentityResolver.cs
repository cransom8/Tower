using CastleDefender.Net;

namespace CastleDefender.Game
{
    public readonly struct MLUnitResolvedIdentity
    {
        public readonly string ArchetypeKey;
        public readonly string PresentationKey;
        public readonly string CatalogUnitKey;
        public readonly string SkinKey;
        public readonly string PortraitKey;
        public readonly string DisplayName;

        public MLUnitResolvedIdentity(
            string archetypeKey,
            string presentationKey,
            string catalogUnitKey,
            string skinKey,
            string portraitKey,
            string displayName)
        {
            ArchetypeKey = archetypeKey;
            PresentationKey = presentationKey;
            CatalogUnitKey = catalogUnitKey;
            SkinKey = skinKey;
            PortraitKey = portraitKey;
            DisplayName = displayName;
        }
    }

    public static class MLUnitPresentationIdentityResolver
    {
        public static MLUnitResolvedIdentity Resolve(MLUnit unit)
        {
            string archetypeKey = !string.IsNullOrWhiteSpace(unit?.archetypeKey)
                ? unit.archetypeKey.Trim()
                : null;
            string presentationKey = !string.IsNullOrWhiteSpace(unit?.presentationKey)
                ? unit.presentationKey.Trim()
                : FortUnitIdentityCatalog.DefaultPresentationKey;
            string catalogUnitKey = !string.IsNullOrWhiteSpace(unit?.catalogUnitKey)
                ? unit.catalogUnitKey.Trim()
                : FortUnitIdentityCatalog.ResolveCatalogUnitKey(
                    archetypeKey,
                    presentationKey,
                    unit?.type,
                    unit?.skinKey);
            string skinKey = !string.IsNullOrWhiteSpace(unit?.skinKey)
                ? unit.skinKey.Trim()
                : FortUnitIdentityCatalog.ResolveSkinKey(
                    archetypeKey,
                    presentationKey,
                    catalogUnitKey,
                    unit?.skinKey);
            string portraitKey = FortUnitIdentityCatalog.ResolvePortraitKey(
                archetypeKey,
                presentationKey,
                catalogUnitKey,
                skinKey);
            string displayName = FortUnitIdentityCatalog.ResolveDisplayName(
                archetypeKey,
                presentationKey,
                catalogUnitKey,
                skinKey,
                unit?.type);

            return new MLUnitResolvedIdentity(
                archetypeKey,
                presentationKey,
                catalogUnitKey,
                skinKey,
                portraitKey,
                displayName);
        }
    }
}
