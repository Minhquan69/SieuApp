using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace V3SClient.models
{
    public struct BoundingBox
    {
      public  OpenCvSharp.Rect Rectangle { get; set; }
        public string Caption { get; set; }
        public OpenCvSharp.Scalar Scalar { get; set; }
        public string Info { get; set; }
        public BoundingBox(OpenCvSharp.Rect rect, OpenCvSharp.Scalar scalar, string caption ="", string info="")
        {
            Rectangle = rect;
            Caption = caption;
            Scalar = scalar;
            Info = info;
        }
        
    }
}