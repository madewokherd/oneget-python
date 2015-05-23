using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PythonProvider
{
    enum VersionClauseType
    {
        Compatible, // ~=
        Matching, // ==
        Exclusion, // !=
        LTE, // <=
        GTE, // >=
        LT, // <
        GT, // >
        ArbitraryEquality // ===
    }

    struct VersionSpecifierClause
    {
        public VersionClauseType type;
        public VersionIdentifier version;

        public bool MatchesVersion(VersionIdentifier version)
        {
            switch (type)
            {
                case VersionClauseType.Compatible:
                    if (version.Compare(this.version) < 0)
                        return false;
                    VersionIdentifier version_prefix = new VersionIdentifier("1");
                    version_prefix.release = new int[this.version.release.Length - 1];
                    for (int i=0; i<this.version.release.Length - 1; i++)
                    {
                        version_prefix.release[i] = this.version.release[i];
                    }
                    return (version_prefix.IsPrefix(version));
                case VersionClauseType.Matching:
                    if (this.version.is_wildcard)
                        return this.version.IsPrefix(version);
                    else
                        return this.version.Compare(version, !this.version.is_localversion, false, false) == 0;
                case VersionClauseType.Exclusion:
                    if (this.version.is_wildcard)
                        return !this.version.IsPrefix(version);
                    else
                        return this.version.Compare(version, !this.version.is_localversion, false, false) != 0;
                case VersionClauseType.GTE:
                    return this.version.Compare(version) <= 0;
                case VersionClauseType.LTE:
                    return this.version.Compare(version) >= 0;
                case VersionClauseType.GT:
                    if (!this.version.is_postrelease && version.is_postrelease &&
                        this.version.Compare(version, false, false, true) == 0)
                        return false;
                    if (version.is_localversion)
                        return false;
                    return this.version.Compare(version) < 0;
                case VersionClauseType.LT:
                    if (!this.version.IsPrerelease && version.IsPrerelease &&
                        this.version.Compare(version, false, true, false) == 0)
                        return false;
                    return this.version.Compare(version) > 0;
                case VersionClauseType.ArbitraryEquality:
                    return this.version.raw_version_string == version.raw_version_string;
                default:
                    return false;
            }
        }
    }

    class VersionSpecifier
    {
        public List<VersionSpecifierClause> clauses;

        public VersionSpecifier()
        {
            clauses = new List<VersionSpecifierClause>();
        }

        public VersionSpecifier(string spec_string) : this()
        {
            ParseSpecifier(spec_string);
        }

        private VersionSpecifierClause ParseSpecifierClause(string clause_string)
        {
            var clause = new VersionSpecifierClause();
            clause_string = clause_string.TrimStart();
            string version_spec = clause_string.TrimStart('=', '~', '<', '>', '!');
            string clause_type_spec = clause_string.Substring(0, clause_string.Length - version_spec.Length);
            bool allow_wildcard = false;
            bool allow_local = false;
            switch (clause_type_spec)
            {
                case "~=":
                    clause.type = VersionClauseType.Compatible;
                    break;
                case "==":
                    clause.type = VersionClauseType.Matching;
                    allow_wildcard = true;
                    allow_local = true;
                    break;
                case "!=":
                    clause.type = VersionClauseType.Exclusion;
                    allow_wildcard = true;
                    allow_local = true;
                    break;
                case "<=":
                    clause.type = VersionClauseType.LTE;
                    break;
                case ">=":
                    clause.type = VersionClauseType.GTE;
                    break;
                case "<":
                    clause.type = VersionClauseType.LT;
                    break;
                case ">":
                    clause.type = VersionClauseType.GT;
                    break;
                case "===":
                    clause.type = VersionClauseType.ArbitraryEquality;
                    break;
                default:
                    throw new ArgumentException(string.Format("invalid operator '{0}'", clause_type_spec));
            }

            clause.version = new VersionIdentifier(version_spec, allow_wildcard);

            if (clause.type != VersionClauseType.ArbitraryEquality && !clause.version.is_valid)
            {
                throw new ArgumentException(string.Format("invalid version '{0}'", version_spec));
            }

            if (clause.type == VersionClauseType.Compatible && clause.version.release.Length == 1)
            {
                throw new ArgumentException("Compatible version (~=) must have at least two segments");
            }

            if (!allow_local && clause.version.is_localversion)
            {
                throw new ArgumentException("Local versions not permitted in version specifiers, other than ==, != and ===");
            }

            return clause;
        }

        public void ParseSpecifier(string spec_string)
        {
            List<VersionSpecifierClause> clauses = new List<VersionSpecifierClause>();
            foreach (string clause in spec_string.Split(','))
            {
                clauses.Add(ParseSpecifierClause(clause));
            }
            this.clauses.AddRange(clauses);
        }

        public bool MatchesVersion(VersionIdentifier version)
        {
            foreach (var clause in clauses)
            {
                if (!clause.MatchesVersion(version))
                    return false;
            }
            return true;
        }

        public bool MatchesVersion(string version)
        {
            return MatchesVersion(new VersionIdentifier(version));
        }
    }
}
