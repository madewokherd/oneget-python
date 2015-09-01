using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PythonProvider
{
    class PackageVersionComparer : Comparer<PythonPackage>
    {
        public override int Compare(PythonPackage x, PythonPackage y)
        {
            return x.version.Compare(y.version);
        }
    }
}
