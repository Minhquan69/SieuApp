using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace V3SClient.libs
{
    /// <summary>
    /// lớp ánh xạ đa hình cho ListBoxMultiSelect
    /// </summary>
    public class MultiSelectFieldMapping
    {
        public string BindingPath { get; set; }
        public Func<object, List<string>> ExtractKeysFromModel { get; set; }
        public Func<List<string>, List<object>> ConvertKeysToObjects { get; set; }
    }
}
