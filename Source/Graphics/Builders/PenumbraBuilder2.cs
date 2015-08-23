﻿using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Penumbra.Mathematics;
using Penumbra.Utilities;

namespace Penumbra.Graphics.Builders
{
    internal class PenumbraBuilder2
    {
        private const float DegreesToRotatePenumbraTowardUmbra = 0.1f;

        private readonly FastList<VertexPosition2Texture> _vertices = new FastList<VertexPosition2Texture>();
        private readonly FastList<int> _indices = new FastList<int>();    
            
        private readonly Pool<PenumbraFin> _finPool = new Pool<PenumbraFin>();
        private readonly List<PenumbraFin> _fins = new List<PenumbraFin>();

        private int _indexOffset;

        public void PreProcess()
        {
            _indexOffset = 0;
            _vertices.Clear();
            _indices.Clear();
        }

        public void ProcessHull(Light light, Hull hull, ref HullContext hullCtx)
        {
            var points = hullCtx.PointContexts;
            for (int i = 0; i < points.Count; i++)
            {
                var ctx = points[i];
                if (ctx.IsConvex && (ctx.RightSide == Side.Right || ctx.Side == Side.Right))
                {
                    _fins.Add(CreateFin(light, ref ctx, hull, Side.Right));
                }
                else if (ctx.IsConvex && (ctx.LeftSide == Side.Left || ctx.Side == Side.Left))
                {
                    _fins.Add(CreateFin(light, ref ctx, hull, Side.Left));
                }
            }

            foreach (PenumbraFin fin in _fins)
            {
                //if (hullCtx.UmbraIntersectionType == IntersectionType.IntersectsInsideLight)
                //{
                //    // CLIP FROM MID                    
                //    ClipMid(fin, ref hullCtx);                    
                //}

                // ADD TEXCOORDS, INTERPOLATE
                AddTexCoords(fin);

                //if (hullCtx.UmbraIntersectionType == IntersectionType.IntersectsInsideLight)
                //{                    
                //    AttachInterpolatedVerticesToContext(fin, ref hullCtx);                    
                //}

                // TRIANGULATE
                TriangulateFin(fin);

                _vertices.AddRange(fin.FinalVertices);
                for (int i = 0; i < fin.Indices.Count; i++)
                {
                    fin.Indices[i] = fin.Indices[i] + _indexOffset;
                }
                _indices.AddRange(fin.Indices);
                _indexOffset += fin.FinalVertices.Count;

                _finPool.Release(fin);                
            }         
            
            // TODO: TEMP   
            hullCtx.UmbraLeftProjectedVertex.TexCoord = hullCtx.UmbraRightProjectedVertex.TexCoord;

            _fins.Clear();
        }

        private void AttachInterpolatedVerticesToContext(PenumbraFin fin, ref HullContext hullCtx)
        {
            foreach (var vertex in fin.FinalVertices)
            {
                // We can populate only 1 vertex from a single fin.
                if (VectorUtil.NearEqual(vertex.Position, hullCtx.UmbraLeftProjectedPoint))
                {
                    hullCtx.UmbraLeftProjectedVertex = vertex;                    
                    return;
                }
                if (VectorUtil.NearEqual(vertex.Position, hullCtx.UmbraRightProjectedPoint))
                {
                    hullCtx.UmbraRightProjectedVertex = vertex;
                    return;
                }
                if (VectorUtil.NearEqual(vertex.Position, hullCtx.UmbraIntersectionPoint))
                {
                    hullCtx.UmbraIntersectionVertex = vertex;                    
                }
            }
        }

        public void Build(Light light, LightVaos vaos)
        {
            if (_vertices.Count > 0 && _indices.Count > 0)
            {
                vaos.HasPenumbra = true;                
                vaos.PenumbraVao.SetVertices(_vertices);
                vaos.PenumbraVao.SetIndices(_indices);                
            } 
            else
            {
                vaos.HasPenumbra = false;
            }            
        }
        
        private PenumbraFin CreateFin(Light light, ref HullPointContext context, Hull hull, Side side)
        {
            PenumbraFin result = _finPool.Fetch();
            result.Reset();
            result.Side = side;            

            // FIND MAIN VERTICES
            PopulateMainVertices(result, light, ref context);
                
            if (light.ShadowType == ShadowType.Occluded)
            {
                // CLIP HULL VERTICES
                ClipHullFromFin(result, hull);
                // REORDER VERTICES FIN ORIGIN FIRST
                OrderFinVerticesOriginFirst(result);
            } 

            return result;
        }        

        private void PopulateMainVertices(PenumbraFin result, Light light, ref HullPointContext context)
        {            
            // ROTATE A LITTLE BIT TOWARD UMBRA TO REMOVE 1 PX INACCURACIES/FLICKERINGS.
            //Vector2 lightRightSideToCurrentDir = VectorUtil.Rotate(context.LightRightToPointDir, -MathHelper.ToRadians(DegreesToRotatePenumbraTowardUmbra));
            //Vector2 lightLeftSideToCurrentDir = VectorUtil.Rotate(context.LightLeftToPointDir, MathHelper.ToRadians(DegreesToRotatePenumbraTowardUmbra));
            Vector2 lightRightSideToCurrentDir = context.LightRightToPointDir;
            Vector2 lightLeftSideToCurrentDir = context.LightLeftToPointDir;
            // CALCULATE RANGE.
            float range = light.Range / Vector2.Dot(context.LightToPointDir, lightRightSideToCurrentDir);

            //int outerTexCoord = context.IsInAnotherHull ? 1 : 0;
            int outerTexCoord = 0;

            result.Vertex1 = new VertexPosition2Texture(context.Point, new Vector2(0, 1));
            result.Vertex3 = new VertexPosition2Texture(
                context.LightRight + lightRightSideToCurrentDir * range,
                new Vector2(result.Side == Side.Left ? outerTexCoord : 1, 0));
            result.Vertex2 = new VertexPosition2Texture(
                context.LightLeft + lightLeftSideToCurrentDir * range,
                new Vector2(result.Side == Side.Left ? 1 : outerTexCoord, 0));

            result.Vertices.Add(result.Vertex1.Position);
            result.Vertices.Add(result.Vertex2.Position);
            result.Vertices.Add(result.Vertex3.Position);
        }

        private static void ClipHullFromFin(PenumbraFin result, Hull hull)
        {            
            Polygon.Clip(result.Vertices, hull.TransformedPoints, result.Vertices);
        }

        private void OrderFinVerticesOriginFirst(PenumbraFin fin)
        {
            int index = 0;
            for (int i = 0; i < fin.Vertices.Count; i++)
            {
                Vector2 pos = fin.Vertices[i];
                if (VectorUtil.NearEqual(pos, fin.Vertex1.Position))
                {
                    index = i;
                    break;
                }
            }

            if (index != 0)
            {
                int vertexCount = fin.Vertices.Count;
                int numToShift = vertexCount - index;                
                fin.Vertices.ShiftRight<Vector2>(numToShift);                
             }
        }

        private void AddTexCoords(PenumbraFin result)
        {
            foreach (Vector2 p in result.Vertices)
            {
                if (VectorUtil.NearEqual(p, result.Vertex1.Position))
                {
                    result.FinalVertices.Add(result.Vertex1);
                }
                else if (VectorUtil.NearEqual(p, result.Vertex2.Position))
                {
                    result.FinalVertices.Add(result.Vertex2);
                }
                else if (VectorUtil.NearEqual(p, result.Vertex3.Position))
                {
                    result.FinalVertices.Add(result.Vertex3);
                }
                else
                {                    
                    Vector2 point = p;
                    Vector3 barycentricCoords;
                    VectorUtil.Barycentric(
                        ref point, 
                        ref result.Vertex1.Position,                         
                        ref result.Vertex2.Position,
                        ref result.Vertex3.Position, 
                        out barycentricCoords);
                    Vector2 interpolatedTexCoord =
                        result.Vertex1.TexCoord * barycentricCoords.X +
                        result.Vertex2.TexCoord * barycentricCoords.Y +
                        result.Vertex3.TexCoord * barycentricCoords.Z;

                    result.FinalVertices.Add(new VertexPosition2Texture(
                        p,
                        interpolatedTexCoord));                        
                }
            }
        }

        private void TriangulateFin(PenumbraFin result)
        {            
            result.Vertices.GetIndices(WindingOrder.Clockwise, result.Indices);            
        }

        private static void ClipMid(PenumbraFin fin, ref HullContext hullCtx)
        {
            if (fin.Side == Side.Left)
            {
                fin.Vertices.Insert(fin.Vertices.Count - 2, hullCtx.UmbraIntersectionPoint);
                fin.Vertices[fin.Vertices.Count - 2] = hullCtx.UmbraRightProjectedPoint;
            }
            else
            {
                fin.Vertices.Insert(2, hullCtx.UmbraLeftProjectedPoint);
                fin.Vertices[3] = hullCtx.UmbraIntersectionPoint;
            }
        }

        private class PenumbraFin
        {
            public readonly FastList<VertexPosition2Texture> FinalVertices = new FastList<VertexPosition2Texture>();
            public readonly Polygon Vertices = new Polygon();
            public readonly FastList<int> Indices = new FastList<int>();

            public VertexPosition2Texture Vertex1; // hull point
            public VertexPosition2Texture Vertex2; // projected left or right
            public VertexPosition2Texture Vertex3; // projected left or right            
            public Side Side;            
 
            public void Reset()
            {                
                FinalVertices.Clear(true);
                Vertices.Clear(true);
                Indices.Clear(true);
            }
        }        
    }    
}