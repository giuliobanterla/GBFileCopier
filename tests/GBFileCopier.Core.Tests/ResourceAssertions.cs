using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace GBFileCopier.Tests;

/// <summary>Checks that a resource class is complete in every language.</summary>
internal static class ResourceAssertions
{
    public static void AssertComplete(Type accessor, ResourceManager resources, string language, params string[] sameInBothLanguages)
    {
        var neutral = Keys(resources, CultureInfo.InvariantCulture);
        var translated = Keys(resources, CultureInfo.GetCultureInfo(language));
        var properties = accessor.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToHashSet();

        Assert.Equal(neutral.Order(), properties.Order()); // every resource has a property and vice versa
        Assert.Equal(neutral.Order(), translated.Order()); // nothing missing or left over in the translation

        foreach (var key in neutral)
        {
            var english = resources.GetString(key, CultureInfo.InvariantCulture);
            var other = resources.GetString(key, CultureInfo.GetCultureInfo(language));
            Assert.False(string.IsNullOrWhiteSpace(other), $"{key} is empty in '{language}'");
            Assert.Equal(Placeholders(english!), Placeholders(other!)); // same {0} {1}...
            if (!sameInBothLanguages.Contains(key))
            {
                Assert.True(english != other, $"{key} is not translated in '{language}'");
            }
        }
    }

    private static HashSet<string> Keys(ResourceManager resources, CultureInfo culture) =>
        resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet();

    private static string[] Placeholders(string text) =>
        [.. System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).Order()];
}
