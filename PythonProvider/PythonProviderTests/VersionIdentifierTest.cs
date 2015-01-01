using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PythonProvider;

namespace PythonProviderTests
{
    [TestClass]
    public class VersionIdentifierTest
    {
        static readonly string[] sorted_version_strings = {
                                                              "0",
                                                              "0.9",
                                                              "1.0",
                                                              "1.1",
                                                              "1.1.1",
                                                              "1.2",
                                                              "2"
                                                          };

        static readonly string[] string_normalizations = { "050.0.06", "50.0.6" };

        [TestMethod]
        public void TestNormalization()
        {
            foreach (var version_string in sorted_version_strings)
            {
                VersionIdentifier ver = new VersionIdentifier(version_string);
                Assert.IsNull(ver.invalid_string, "version string {0} rejected by parser", version_string);
                Assert.AreEqual(ver.ToString(), version_string, "version string did not normalize to itself");
            }

            int i;
            for (i = 0; i < string_normalizations.Length; i+=2)
            {
                VersionIdentifier ver = new VersionIdentifier(string_normalizations[i]);
                Assert.IsNull(ver.invalid_string, "version string {0} rejected by parser", string_normalizations[0]);
                Assert.AreEqual(ver.ToString(), string_normalizations[i + 1], "expected {0} to normalize to {1}", string_normalizations[i], string_normalizations[i + 1]);
            }
        }
    }
}
