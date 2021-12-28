using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using System.Collections;

namespace FamilyParametricCreate
{

    [Transaction(TransactionMode.Manual)]
    public class FamilyParametricCreate : IExternalCommand
    {
        public UIDocument activeUIDoc;

        public Document activeDoc;
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            activeUIDoc = commandData.Application.ActiveUIDocument;
            activeDoc = activeUIDoc.Document;
            using (Transaction tran = new Transaction(activeDoc, "default"))
            {
                tran.Start();
                CreateParts();
                tran.Commit();
            }
            return Result.Succeeded;
        }

        public bool CreateParts()
        {   
            FloorSelectionFilter floorSelectionFilter = new FloorSelectionFilter();
            Reference faceRefer = activeUIDoc.Selection.PickObject(ObjectType.Element, floorSelectionFilter);
            if (faceRefer == null)
            {
                return false;
            }
            Floor selFloor = activeDoc.GetElement(faceRefer) as Floor;
            PartUtils.CreateParts(activeDoc, new List<ElementId> { selFloor.Id});
            // DivideParts中的第二个参数(需要被分割的零件实体)
            List<ElementId> readyToDivideEleIdList = PartUtils.GetAssociatedParts(activeDoc, selFloor.Id, true, true).ToList();
            // 获取floor的底面
            //GeometryObject selFloorGemObj = selFloor.GetGeometryObjectFromReference(faceRefer);
                
            GeometryElement selFloorGemEle = selFloor.get_Geometry(new Options());
            Solid mainSolid = null;
            foreach (GeometryObject geometryObject in selFloorGemEle)
            {
                if (geometryObject is Solid)
                {
                    mainSolid = (Solid)geometryObject;
                    break;
                }
            }
            if (mainSolid == null)
            {
                return false;
            }
            // 第五个参数 草图面
            SketchPlane sketchPlane = null;
            IList<Curve> buttomFaceCurves = new List<Curve>();
            foreach (Face face in mainSolid.Faces)
            {   
                Plane currentFacePlane = face.GetSurface() as Plane; 
                if (currentFacePlane.Normal.Z >= 1)
                {
                    IEnumerator edgeLoopsEnumerator = face.EdgeLoops.GetEnumerator();
                    EdgeArray mainEdgeArray = null;
                    while (edgeLoopsEnumerator.MoveNext())
                    {
                        mainEdgeArray = edgeLoopsEnumerator.Current as EdgeArray;
                        break;
                    }
                    if (mainEdgeArray == null)
                    {
                        return false;
                    }
                    foreach (Edge edge in mainEdgeArray)
                    {
                        buttomFaceCurves.Add(edge.AsCurve());
                    }
                    sketchPlane = SketchPlane.Create(activeDoc, currentFacePlane);
                }
            }

            // 分割线生成 长边的中点
            Curve longerCurve = null;
            Line splitCurve = null;
            if ((buttomFaceCurves[0] as Line).Length >= (buttomFaceCurves[1] as Line).Length)
            {
                splitCurve = Line.CreateBound((buttomFaceCurves[0] as Line).Evaluate(0.5, true), (buttomFaceCurves[2] as Line).Evaluate(0.5, true));
            }
            else
            {
                splitCurve = Line.CreateBound((buttomFaceCurves[1] as Line).Evaluate(0.5, true), (buttomFaceCurves[3] as Line).Evaluate(0.5, true));
            }

            PartUtils.DivideParts(activeDoc, readyToDivideEleIdList, new List<ElementId>() { }, new List<Curve>() { splitCurve }, sketchPlane.Id );

            return true;
        }
    }
}
