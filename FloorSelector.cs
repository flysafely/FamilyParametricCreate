using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;

namespace FamilyParametricCreate
{
    public class FloorSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is Floor;
        }
        public bool AllowReference(Reference refer, XYZ point)
        {
            return false;
        }
    }
}
