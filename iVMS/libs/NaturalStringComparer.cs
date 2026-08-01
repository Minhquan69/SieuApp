using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    public class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            return StrCmpLogicalW(x, y);
        }

        [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string s1, string s2);
    }
}















