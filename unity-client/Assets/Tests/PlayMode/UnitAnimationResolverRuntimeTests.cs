using System;
using System.Reflection;
using NUnit.Framework;

public class UnitAnimationResolverRuntimeTests
{
    [Test]
    public void ResolveAttackPulseStates_Rotates_Melee_Specials_From_ServerAttackPulses()
    {
        object profile = CreateResolvedProfile(
            "Melee",
            "Attack1",
            "Attack2",
            "SpecialAttack1",
            "SpecialAttack2");

        AssertPrimaryAttackState(profile, 1, "Attack1");
        AssertPrimaryAttackState(profile, 2, "Attack2");
        AssertPrimaryAttackState(profile, 3, "Attack1");
        AssertPrimaryAttackState(profile, 5, "SpecialAttack1");
        AssertPrimaryAttackState(profile, 10, "SpecialAttack2");
    }

    [Test]
    public void ResolveAttackPulseStates_Prefers_Support_Casts_And_Specials_When_Available()
    {
        object profile = CreateResolvedProfile(
            "Support",
            "Cast",
            "Attack1",
            "Attack2",
            "SpecialAttack1",
            "SpecialAttack2");

        AssertPrimaryAttackState(profile, 1, "Cast");
        AssertPrimaryAttackState(profile, 2, "Attack1");
        AssertPrimaryAttackState(profile, 5, "SpecialAttack1");
        AssertPrimaryAttackState(profile, 7, "SpecialAttack2");
    }

    static void AssertPrimaryAttackState(object profile, int attackPulse, string expectedState)
    {
        Type resolverType = FindType("CastleDefender.Game.UnitAnimationResolver");
        Assert.That(resolverType, Is.Not.Null, "UnitAnimationResolver should be discoverable in the loaded runtime assemblies.");

        MethodInfo resolveMethod = resolverType.GetMethod(
            "ResolveAttackPulseStates",
            BindingFlags.Public | BindingFlags.Static);
        Assert.That(resolveMethod, Is.Not.Null, "ResolveAttackPulseStates should remain available for runtime attack sequencing.");

        string[] stateNames = (string[])resolveMethod.Invoke(null, new[] { profile, null, (object)attackPulse });

        Assert.That(stateNames, Is.Not.Null.And.Not.Empty);
        Assert.That(stateNames[0], Is.EqualTo(expectedState));
    }

    static object CreateResolvedProfile(string attackFamilyName, params string[] attackStates)
    {
        Type resolverType = FindType("CastleDefender.Game.UnitAnimationResolver");
        Type attackFamilyType = FindType("CastleDefender.Game.UnitAnimationAttackFamily");
        Type resolvedProfileType = resolverType?.GetNestedType("ResolvedProfile", BindingFlags.Public);

        Assert.That(resolverType, Is.Not.Null, "UnitAnimationResolver should be discoverable in the loaded runtime assemblies.");
        Assert.That(attackFamilyType, Is.Not.Null, "UnitAnimationAttackFamily should be discoverable in the loaded runtime assemblies.");
        Assert.That(resolvedProfileType, Is.Not.Null, "ResolvedProfile should stay public for runtime animation resolution.");

        object profile = Activator.CreateInstance(resolvedProfileType);
        PropertyInfo attackFamilyProperty = resolvedProfileType.GetProperty("AttackFamily", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo attackStatesProperty = resolvedProfileType.GetProperty("AttackStates", BindingFlags.Instance | BindingFlags.Public);

        Assert.That(attackFamilyProperty, Is.Not.Null);
        Assert.That(attackStatesProperty, Is.Not.Null);

        attackFamilyProperty.SetValue(profile, Enum.Parse(attackFamilyType, attackFamilyName));
        attackStatesProperty.SetValue(profile, attackStates);
        return profile;
    }

    static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }
}
