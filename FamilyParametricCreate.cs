using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI.Selection;
using Autodesk.Revit.DB.Structure;
using System.Collections;
using CMCUAPI.Extends;

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
            ICollection<ElementId> elementIds = null;
            using (Transaction tran = new Transaction(activeDoc, "default"))
            {
                tran.Start();
                elementIds = CreateParts();
                tran.Commit();

            }
            foreach (ElementId eleId in elementIds)
            {
                CreateRebarFromCurves(activeDoc, activeDoc.GetElement(eleId));
            }

            return Result.Succeeded;
        }

        public ICollection<ElementId> CreateParts()
        {   
            FloorSelectionFilter floorSelectionFilter = new FloorSelectionFilter();
            Reference floorRefer = activeUIDoc.Selection.PickObject(ObjectType.Element, floorSelectionFilter);
            if (floorRefer == null)
            {
                return null;
            }
            Floor selFloor = activeDoc.GetElement(floorRefer) as Floor;
            PartUtils.CreateParts(activeDoc, new List<ElementId> { selFloor.Id});
            activeDoc.Regenerate();
            // DivideParts中的第二个参数(需要被分割的零件实体)
            ICollection<ElementId> readyToDivideEleIdList = PartUtils.GetAssociatedParts(activeDoc, selFloor.Id, false, false);
            //TaskDialog.Show("note", readyToDivideEleIdList.ToList()[0].ToString());
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
                return null;
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
                        return null;
                    }
                    foreach (Edge edge in mainEdgeArray)
                    {
                        buttomFaceCurves.Add(edge.AsCurve());
                    }
                    sketchPlane = SketchPlane.Create(activeDoc, currentFacePlane);
                }
            }

            // 分割线生成 长边的中点
            Line splitCurve = null;
            if ((buttomFaceCurves[0] as Line).Length >= (buttomFaceCurves[1] as Line).Length)
            {   
                // 内部分割线(两端在边缘上)
                
                XYZ direction = (buttomFaceCurves[1] as Line).Direction;
                splitCurve = Line.CreateBound((buttomFaceCurves[0] as Line).Evaluate(0.5, true) - direction, (buttomFaceCurves[2] as Line).Evaluate(0.5, true) + direction);
            }
            else
            {
                XYZ direction0 = (buttomFaceCurves[0] as Line).Direction;
                XYZ direction2 = (buttomFaceCurves[2] as Line).Direction;
                splitCurve = Line.CreateBound((buttomFaceCurves[1] as Line).Evaluate(0.5, true) + direction0, (buttomFaceCurves[3] as Line).Evaluate(0.5, true) + direction2);
            }

            PartMaker partMaker = PartUtils.DivideParts(activeDoc, readyToDivideEleIdList, new List<ElementId>() { }, new List<Curve>() { splitCurve }, sketchPlane.Id );

            PartUtils.GetPartMakerMethodToDivideVolumeFW(partMaker).DivisionGap = UnitUtils.ConvertToInternalUnits(200, DisplayUnitType.DUT_MILLIMETERS);
            activeDoc.Regenerate();
            //TaskDialog.Show("note", readyToDivideEleIdList.ToList()[0].ToString());
            //TaskDialog.Show("note", PartUtils.GetAssociatedParts(activeDoc, selFloor.Id, false, true).Count.ToString());
            ICollection<ElementId> elementIds = PartUtils.GetAssociatedParts(activeDoc, selFloor.Id, false, true);
            return elementIds;

        }
        public List<Curve> GetButtomFaceCurveList(Element floorslabEle)
        {
            //Face face = floorslabEle.GetFaces().FirstOrDefault(p => (p as PlanarFace).FaceNormal.IsAlmostEqualTo(XYZ.BasisZ));
            //List<Curve> curves = face.GetCurveLoopMax();
            //return curves;
            Options options = new Options
            {
                ComputeReferences = true
            };
            GeometryElement geometryElement = floorslabEle.get_Geometry(options);
            Solid mainSolid = null;
            foreach (GeometryObject gemObject in geometryElement)
            {
                Solid solid = gemObject as Solid;
                if (solid != null && solid.Volume != 0 && solid.SurfaceArea != 0)
                {
                    mainSolid = solid;
                    break;
                }
            }
            Face btFace = null;
            foreach (Face face in mainSolid.Faces)
            {
                Plane plane = face.GetSurface() as Plane;
                if (plane.Normal.Z == -1)
                {
                    btFace = face;
                }
            }

            var edgeArrayEnumerator = btFace.EdgeLoops.GetEnumerator();
            EdgeArray edgeArray = null;
            while (edgeArrayEnumerator.MoveNext())
            {
                edgeArray = edgeArrayEnumerator.Current as EdgeArray;
                break;
            }

            List<Curve> curveList = new List<Curve>();
            var edgeEnumerator = edgeArray.GetEnumerator();
            while (edgeEnumerator.MoveNext())
            {
                curveList.Add((edgeEnumerator.Current as Edge).AsCurve());
            }
            curveList.SortCurveAsLoop();
            return curveList;

        }
        public static Family LoadFamily(Document doc, string familyName)
        {
            var family = new FilteredElementCollector(doc).OfClass(typeof(Family)).Select(p => p as Family).FirstOrDefault(p => p.Name == familyName);
            string familyPath = string.Format("C:\\ProgramData\\Autodesk\\RVT 2021\\Libraries\\Chinese\\结构\\钢筋形状\\{0}.rfa", familyName);
            if (family == null)
            {
                // 重新载入
                /*                var paths = Directory.GetFiles(familyPath, familyName + ".rfa", SearchOption.AllDirectories);
                                if (paths.Count() == 0)
                                    return null;
                                var path = paths.First();*/
                bool loadResult = doc.LoadFamily(familyPath, new FamilyLoadOptions(), out family);
                if (!loadResult)
                    throw new Exception("族加载失败!");
                foreach (FamilySymbol fs in family.GetFamilySymbolIds().Select(p => doc.GetElement(p)))
                {
                    fs.Activate();
                }
            }
            return family;
        }
        public void CreateRebarFromCurves(Document doc, Element hostElement)
        {
            using (Transaction trans = new Transaction(doc, "创建钢筋"))
            {
                trans.Start("Create Rebar From Curves");

                List<Curve> curves = GetButtomFaceCurveList(hostElement);

                XYZ dir1 = curves[0].GetEndPoint(1) - curves[0].GetEndPoint(0);
                XYZ dir2 = curves[1].GetEndPoint(1) - curves[1].GetEndPoint(0);
                XYZ normal = dir1.CrossProduct(dir2);

                //RebarBarType bartype = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).FirstElement() as RebarBarType;
                //RebarBarType bartype = new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).FirstOrDefault(t => t.Name == "8 HPB300") as RebarBarType;
                FilteredElementCollector rbTypeCol = new FilteredElementCollector(doc);
                rbTypeCol.OfClass(typeof(RebarBarType));
                IEnumerable<RebarBarType> rTypes = from elem in rbTypeCol
                                                   let r = elem as RebarBarType
                                                   where r.Name == "10 HRB400"
                                                   select r;

                if (!rTypes.Any())
                {
                    LoadFamily(doc, "01");
                    rbTypeCol.OfClass(typeof(RebarBarType));
                    rTypes = from elem in rbTypeCol
                             let r = elem as RebarBarType
                             where r.Name == "10 HRB400"
                             select r;
                }
                // 随便创建一些钢筋...
                RebarBarType type = rTypes.First();
                Line line1 = curves[1] as Line;
                int conut1 = (int)(line1.Length / UnitUtils.ConvertToInternalUnits(150, DisplayUnitType.DUT_MILLIMETERS));
                Rebar newRebar = Rebar.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, hostElement, (curves[1] as Line).Direction, curves.GetRange(0, 1), RebarHookOrientation.Left, RebarHookOrientation.Right, false, true);
                //newRebar.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(20, 3, true, true, true);
                newRebar.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(conut1, UnitUtils.ConvertToInternalUnits(150, DisplayUnitType.DUT_MILLIMETERS), true, true, true);
                
                Line line2 = curves[0] as Line;
                int conut2 = (int)(line2.Length / UnitUtils.ConvertToInternalUnits(150, DisplayUnitType.DUT_MILLIMETERS));
                Rebar newRebar2 = Rebar.CreateFromCurves(doc, RebarStyle.Standard, type, null, null, hostElement, (curves[2] as Line).Direction, curves.GetRange(1, 1), RebarHookOrientation.Left, RebarHookOrientation.Right, false, true);
                newRebar2.GetShapeDrivenAccessor().SetLayoutAsNumberWithSpacing(conut2, UnitUtils.ConvertToInternalUnits(150, DisplayUnitType.DUT_MILLIMETERS), true, true, true);
                trans.Commit();
            }
        }
    }
}
