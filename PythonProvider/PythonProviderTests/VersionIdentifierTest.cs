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
                                                              "1.0a0.post4.dev1+postrelease10",
                                                              "1.0a0.post4.dev1+postrelease3",
                                                              "1.0a0.post4.dev1+postrelease3.2",
                                                              "1.0a0.post4.dev1+2",
                                                              "1.0a0.post4.dev1+2.3",
                                                              "1.0a0.post4.dev1+2.10",
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
                                                              "1.0+1.0is02.0",
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
                                                             "1.0-post2", "1.0.post2",
                                                             "1.0r2", "1.0.post2",
                                                             "1.0-rev_2", "1.0.post2",
                                                             "1.0-1", "1.0.post1",
                                                             "1.0dev2", "1.0.dev2",
                                                             "1.0_dev3", "1.0.dev3",
                                                             "1.0-dev3", "1.0.dev3",
                                                             "1.0+1-0_1.1", "1.0+1.0.1.1",
                                                             "v2", "2"
                                                         };

        [TestMethod]
        public void TestNormalization()
        {
            foreach (var version_string in sorted_version_strings)
            {
                VersionIdentifier ver = new VersionIdentifier(version_string);
                Assert.IsTrue(ver.is_valid, "version string {0} rejected by parser", version_string);
                Assert.AreEqual(ver.ToString(), version_string, "version string did not normalize to itself");
            }

            int i;
            for (i = 0; i < string_normalizations.Length; i+=2)
            {
                VersionIdentifier ver = new VersionIdentifier(string_normalizations[i]);
                Assert.IsTrue(ver.is_valid, "version string {0} rejected by parser", string_normalizations[i]);
                Assert.AreEqual(ver.ToString(), string_normalizations[i + 1], "expected {0} to normalize to {1}", string_normalizations[i], string_normalizations[i + 1]);
            }
        }

        [TestMethod]
        public void TestComparison()
        {
            VersionIdentifier[] identifiers = new VersionIdentifier[sorted_version_strings.Length];
            int res;

            for (int i=0; i<identifiers.Length; i++)
            {
                identifiers[i] = new VersionIdentifier(sorted_version_strings[i]);
            }

            for (int i=0; i<identifiers.Length; i++)
            {
                for (int j=0; j<identifiers.Length; j++)
                {
                    res = identifiers[i].Compare(identifiers[j]);
                    if (i < j)
                        Assert.AreEqual(-1, res, "expected {0} < {1}, got {2}", sorted_version_strings[i], sorted_version_strings[j], res);
                    else if (i == j)
                        Assert.AreEqual(0, res, "expected {0} = {1}, got {2}", sorted_version_strings[i], sorted_version_strings[j], res);
                    else
                        Assert.AreEqual(1, res, "expected {0} > {1}, got {2}", sorted_version_strings[i], sorted_version_strings[j], res);
                }
            }

            res = new VersionIdentifier("1.0").Compare("1.0.0");
            Assert.AreEqual(0, res, "expected 1.0 = 1.0.0, got {0}", res);
        }

        [TestMethod]
        public void TestPrefix()
        {
            VersionIdentifier v = new VersionIdentifier("1.0");

            Assert.IsTrue(v.IsPrefix("1.0.0"));
            Assert.IsTrue(v.IsPrefix("1.0.1"));
            Assert.IsTrue(v.IsPrefix("1.0a2"));
            Assert.IsTrue(v.IsPrefix("1"));
            Assert.IsFalse(v.IsPrefix("1.1"));
            Assert.IsFalse(v.IsPrefix("2:1.0"));
            Assert.IsFalse(v.IsPrefix("2.0"));

            v = new VersionIdentifier("1.0.1");

            Assert.IsTrue(v.IsPrefix("1.0.1"));
            Assert.IsTrue(v.IsPrefix("1.0.1post2"));
            Assert.IsTrue(v.IsPrefix("1.0.1dev2"));
            Assert.IsFalse(v.IsPrefix("1.0"));
        }
    }
}
