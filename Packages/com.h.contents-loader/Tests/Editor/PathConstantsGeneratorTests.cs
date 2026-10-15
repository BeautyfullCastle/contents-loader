using NUnit.Framework;

namespace H.ContentsLoader.Editor.Tests
{
    public class PathConstantsGeneratorTests
    {
        [Test]
        public void NestsConstantsByFolder()
        {
            var code = PathConstantsGenerator.Generate(new[] { "Towers/Cannon", "Towers/Fire/Mortar", "Config" });

            StringAssert.Contains("public static class ContentPaths", code);
            StringAssert.Contains("public const string Config = \"Config\";", code);
            StringAssert.Contains("public static class Towers", code);
            StringAssert.Contains("public const string Cannon = \"Towers/Cannon\";", code);
            StringAssert.Contains("public static class Fire", code);
            StringAssert.Contains("public const string Mortar = \"Towers/Fire/Mortar\";", code);
        }

        [Test]
        public void SanitizesNamesThatAreNotIdentifiers()
        {
            var code = PathConstantsGenerator.Generate(new[] { "Season 01/2nd-Boss" });

            StringAssert.Contains("public static class Season_01", code);
            StringAssert.Contains("public const string _2nd_Boss = \"Season 01/2nd-Boss\";", code);
        }

        [Test]
        public void AvoidsMemberNamedLikeItsClass()
        {
            var code = PathConstantsGenerator.Generate(new[] { "Towers/Towers", "Towers", "Towers/Inner/x" });

            StringAssert.Contains("public const string Towers_ = \"Towers/Towers\";", code);
            StringAssert.Contains("public const string Towers_ = \"Towers\";", code);
        }
    }
}
