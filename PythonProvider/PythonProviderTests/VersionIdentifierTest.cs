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
                                                              "1.0.dev23",
                                                              "1.0a0.dev2",
                                                              "1.0a0",
                                                              "1.0a0.post4.dev1",
                                                              "1.0a0.post4.dev1+postrelease",
                                                              "1.0a0.post4",
                                                              "1.0a5",
                                                              "1.0b2.dev3",
                                                              "1.0b2",
                                                              "1.0b2.post3.dev4",
                                                              "1.0b2.post3",
                                                              "1.0rc1.dev5",
                                                              "1.0rc1",
                                                              "1.0rc1.post2.dev6",
                                                              "1.0rc1.post2",
                                                              "1.0",
                                                              "1.0+1.0is2.01",
                                                              "1.0.post1.dev7",
                                                              "1.0.post1",
                                                              "1.1",
                                                              "1.1.1",
                                                              "1.2",
                                                              "2",
                                                              "1!0.5"
                                                          };

        static readonly string[] string_normalizations = {
                                                             "050.0.06", "50.0.6",
                                                             "1.0alpha2", "1.0a2",
                                                             "1.0a", "1.0a0",
                                                             "1.0beta", "1.0b0",
                                                             "1.0c2", "1.0rc2",
                                                             "1.0pre2", "1.0rc2",
                                                             "1.0preview2", "1.0rc2",
                                                             "1.0.post", "1.0.post0",
                                                             "1.0.post.dev", "1.0.post0.dev0",
                                                             "0!1", "1",
                                                             "\t1.0RC1 ", "1.0rc1",
                                                             "01!2", "1!2",
                                                             "1.0.a0010", "1.0a10",
                                                             "1.2-b", "1.2b0",
                                                             "1.2_c0", "1.2rc0",
                                                             "2.1rc-5", "2.1rc5",
                                                             "1.0.post-5", "1.0.post5",
                                                             "1.0post", "1.0.post0",
                                                             "1.0_post", "1.0.post0",
                                                             "1.0-post2", "1.0.post2"
                                                         };

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
                Assert.IsNull(ver.invalid_string, "version string {0} rejected by parser", string_normalizations[i]);
                Assert.AreEqual(ver.ToString(), string_normalizations[i + 1], "expected {0} to normalize to {1}", string_normalizations[i], string_normalizations[i + 1]);
            }
        }
    }
}
